using Atelier.Framework.Primitives;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Atelier.Framework.Performance;
using Atelier.Framework.Attache.Audit;
using Atelier.Framework.Context;
using Atelier.Framework.Facility;
using Atelier.Framework.Infrastructure;
using Atelier.Framework.Attributes;
using Atelier.Framework.Infrastructure.Operation;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;
using Atelier.Framework.Host.Execution;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Atelier.Framework.Attache;

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class AttacheHost : IAtelier, IAttache, IHostedService, IAsyncDisposable
{
    [Requisite] protected readonly IRequisitionService _requisitionService = null!;
    [Requisite] protected readonly IContextAccessor _contextAccessor = null!;
    [Requisite] protected readonly ICapabilityAuditChannel _auditChannel = null!;
    [Requisite] protected readonly HealthCheckService _healthCheckService = null!;

    private readonly ConcurrentDictionary<string, byte> _consumerGates = new();
    private readonly ConcurrentDictionary<string, (string RequirementId, string CapabilityName, string ConsumerId)> _grantedTickets = new();
    private readonly ConcurrentDictionary<Guid, Func<CapabilityNotice, CancellationToken, Task>> _noticeHandlers = new();

    private readonly StrongBox<AttacheConfiguration> _configuration = new(new AttacheConfiguration());
    private readonly StrongBox<int> _state = new((int)AttacheState.Created);

    private readonly ProcessResourceSampler _sampler = new();

    public string InstanceId { get; } = Guid.NewGuid().ToString();
    public AttacheState State => (AttacheState)Volatile.Read(ref _state.Value);
    public AttacheConfiguration Configuration => _configuration.Value!;

    public Outcome Configure(AttacheConfiguration configuration)
    {
        if (configuration is null)
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", "Configuration was null"), ("InstanceId", InstanceId)]);
            return Outcome.Failure();
        }

        var observed = (AttacheState)Interlocked.CompareExchange(
            ref _state.Value,
            (int)AttacheState.Created,
            (int)AttacheState.Created);

        if (observed != AttacheState.Created)
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", "Cannot configure AttacheHost from current state"), ("InstanceId", InstanceId), ("State", observed.ToString())]);
            return Outcome.Failure();
        }

        _configuration.Value = configuration;

        Observe(LogLevel.Information, values: [("InstanceId", InstanceId)]);

        return Outcome.Success();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(
                ref _state.Value,
                (int)AttacheState.Running,
                (int)AttacheState.Created) != (int)AttacheState.Created)
        {
            return Task.CompletedTask;
        }

        var verification = _auditChannel.VerifyChain();
        if (!verification.IsIntact)
        {
            Observe(LogLevel.Error, values: [("InstanceId", InstanceId), ("AuditChainIntact", false), ("FirstBreakSequence", verification.FirstBreakSequence ?? -1), ("FirstBreakReason", verification.FirstBreakReason ?? string.Empty), ("AnchorSequence", verification.AnchorSequence)]);
        }

        Observe(LogLevel.Information, values: [("InstanceId", InstanceId)]);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(
                ref _state.Value,
                (int)AttacheState.Stopped,
                (int)AttacheState.Running) != (int)AttacheState.Running)
        {
            return Task.CompletedTask;
        }

        Observe(LogLevel.Information, values: [("InstanceId", InstanceId)]);

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _sampler.DisposeAsync().ConfigureAwait(false);
    }

    [Operation("RequestCapabilityAsync")]
    public async Task<Outcome<CapabilityGrant>> RequestCapabilityAsync(
        CapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        var principal = CapturePrincipal();

        if (cancellationToken.IsCancellationRequested)
        {
            return DenyCancelledRequest(
                principal,
                request?.ConsumerId ?? string.Empty,
                request?.CapabilityTypeName ?? string.Empty);
        }

        if (request is null)
        {
            _auditChannel.RecordDenial(
                principal,
                string.Empty,
                string.Empty,
                "INVALID_ARGUMENT",
                $"{nameof(request)} cannot be null");
            return Outcome<CapabilityGrant>.Failure();
        }

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "Consumer", request.ConsumerId ?? string.Empty);

        if (string.IsNullOrEmpty(request.ConsumerId))
        {
            _auditChannel.RecordDenial(
                principal,
                string.Empty,
                request.CapabilityTypeName,
                "INVALID_CONSUMER",
                "Consumer identity is required");
            return Outcome<CapabilityGrant>.Failure();
        }

        if (!principal.IsAuthenticated
            || string.IsNullOrEmpty(principal.UserId))
        {
            _auditChannel.RecordDenial(
                principal,
                request.ConsumerId,
                request.CapabilityTypeName,
                "UNAUTHORIZED",
                "Capability requests require a verified principal");
            return Outcome<CapabilityGrant>.Failure();
        }

        if (!string.Equals(request.ConsumerId, principal.UserId, StringComparison.Ordinal))
        {
            _auditChannel.RecordDenial(
                principal,
                request.ConsumerId,
                request.CapabilityTypeName,
                "FORBIDDEN",
                "Consumer identity does not match the verified principal");
            return Outcome<CapabilityGrant>.Failure();
        }

        var capabilityType = request.CapabilityType;
        if (capabilityType is null)
        {
            if (request.CapabilityTypeName.Contains(',', StringComparison.Ordinal))
            {
                _auditChannel.RecordDenial(
                    principal,
                    request.ConsumerId,
                    request.CapabilityTypeName,
                    "INVALID_CAPABILITY_TYPE",
                    "Capability type name must not be assembly-qualified");
                return Outcome<CapabilityGrant>.Failure();
            }

            capabilityType = SafeTypeResolver.Resolve(request.CapabilityTypeName);
        }

        if (capabilityType is null)
        {
            _auditChannel.RecordDenial(
                principal,
                request.ConsumerId,
                request.CapabilityTypeName,
                "INVALID_CAPABILITY_TYPE",
                "Requested capability type could not be resolved");
            return Outcome<CapabilityGrant>.Failure();
        }

        if (!_consumerGates.TryAdd(request.ConsumerId, 0))
        {
            _auditChannel.RecordDenial(
                principal,
                request.ConsumerId,
                capabilityType.Name,
                "RATE_LIMITED",
                "Consumer has too many in-flight capability requests");
            return Outcome<CapabilityGrant>.Failure();
        }

        try
        {
            var requirement = new CapabilityRequirement
            {
                RequiredType = capabilityType,
                Scope = RequirementScope.Capability,
                ResourceNeeds = request.ResourceNeeds ?? new ResourceAllocation(),
                Constraints = BuildConstraints(request)
            };

            var provisionResult = await _requisitionService.ProvisionAsync(
                requirement,
                cancellationToken).ConfigureAwait(false);

            if (!provisionResult.IsSuccess)
            {
                _auditChannel.RecordDenial(
                    principal,
                    request.ConsumerId,
                    capabilityType.Name,
                    "PROVISION_FAILED",
                    "Capability provisioning failed");
                return Outcome<CapabilityGrant>.Failure();
            }

            var ticket = provisionResult.Data;
            _grantedTickets[ticket.TicketId] = (requirement.RequirementId, capabilityType.Name, request.ConsumerId);

            var grant = new CapabilityGrant
            {
                ConsumerId = request.ConsumerId,
                CapabilityName = capabilityType.Name,
                GatewayEndpoint = ticket.GatewayEndpoint,
                GatewayPort = ticket.GatewayPort,
                Credentials = ticket.Credentials,
                TicketId = ticket.TicketId
            };

            _auditChannel.RecordGrant(
                principal,
                request.ConsumerId,
                capabilityType.Name,
                ticket.TicketId);

            Observe(LogLevel.Information, values: [("ConsumerId", request.ConsumerId), ("Capability", capabilityType.Name), ("TicketId", ticket.TicketId)]);

            await DeliverNoticeAsync(
                new CapabilityNotice
                {
                    TicketId = ticket.TicketId,
                    CapabilityName = capabilityType.Name,
                    Kind = CapabilityNoticeKind.Provisioned,
                    GatewayEndpoint = ticket.GatewayEndpoint
                },
                cancellationToken).ConfigureAwait(false);

            return grant;
        }
        finally
        {
            _consumerGates.TryRemove(request.ConsumerId, out _);
        }
    }

    [Operation("ReleaseCapabilityAsync")]
    public async Task<Outcome> ReleaseCapabilityAsync(
        string ticketId,
        CancellationToken cancellationToken = default)
    {
        var principal = CapturePrincipal();

        if (cancellationToken.IsCancellationRequested)
        {
            return DenyCancelledRelease(principal);
        }

        if (string.IsNullOrEmpty(ticketId))
        {
            _auditChannel.RecordDenial(
                principal,
                string.Empty,
                string.Empty,
                "INVALID_ARGUMENT",
                $"{nameof(ticketId)} cannot be null or empty");
            return Outcome.Failure();
        }

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "Ticket", ticketId);

        if (!_grantedTickets.TryRemove(ticketId, out var grant))
        {
            Observe(
                LogLevel.Information,
                values: [("Message", "Release of absent grant treated as success"), ("TicketId", ticketId)]);
            return Outcome.Success();
        }

        var releaseResult = await _requisitionService.ReleaseAsync(
            grant.RequirementId,
            cancellationToken).ConfigureAwait(false);

        if (releaseResult.IsSuccess)
        {
            await DeliverNoticeAsync(
                new CapabilityNotice
                {
                    TicketId = ticketId,
                    CapabilityName = grant.CapabilityName,
                    Kind = CapabilityNoticeKind.Released
                },
                cancellationToken).ConfigureAwait(false);
        }

        _auditChannel.RecordRelease(
            principal,
            grant.ConsumerId,
            grant.CapabilityName,
            ticketId,
            releaseResult.IsSuccess
                ? "RELEASED"
                : "RELEASE_FAILED");

        Observe(LogLevel.Information, values: [("TicketId", ticketId)]);

        return releaseResult;
    }

    [Operation("GetHealthReportAsync")]
    public async Task<Outcome<AttacheHealthReport>> GetHealthReportAsync(
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome<AttacheHealthReport>.Failure();
        }

        var state = State;
        var checkReport = await _healthCheckService.CheckHealthAsync(cancellationToken).ConfigureAwait(false);

        var issues = new List<string>();
        foreach (var entry in checkReport.Entries)
        {
            if (entry.Value.Status != HealthStatus.Healthy)
            {
                issues.Add($"{entry.Key}: {entry.Value.Status} ({entry.Value.Description ?? "no description"})");
            }
        }

        var report = new AttacheHealthReport
        {
            InstanceId = InstanceId,
            State = state,
            IsHealthy = state == AttacheState.Running
                && checkReport.Status == HealthStatus.Healthy,
            Timestamp = DateTime.UtcNow,
            ResourceUsage = new AttacheResourceUsage
            {
                MemoryUsageBytes = _sampler.Current.WorkingSetBytes,
                CpuUsagePercent = _sampler.Current.CpuUsagePercent
            },
            Boutiques = new List<BoutiqueHealthReport>(),
            Issues = issues
        };

        return Outcome<AttacheHealthReport>.Success(report);
    }

    public IDisposable SubscribeNotices(Func<CapabilityNotice, CancellationToken, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var subscriptionId = Guid.NewGuid();
        _noticeHandlers[subscriptionId] = handler;

        return new NoticeSubscription(() => _noticeHandlers.TryRemove(subscriptionId, out _));
    }

    [Operation("DeliverNoticeAsync")]
    public async Task<Outcome> DeliverNoticeAsync(
        CapabilityNotice notice,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome.Failure();
        }

        if (notice is null)
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", "Notice was null")]);
            return Outcome.Failure();
        }

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "Ticket", notice.TicketId);

        foreach (var handler in _noticeHandlers.Values)
        {
            try
            {
                await handler(notice, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Observe(LogLevel.Warning, ex, values: [("TicketId", notice.TicketId)]);
            }
        }

        Observe(LogLevel.Information, values: [("TicketId", notice.TicketId), ("Kind", notice.Kind.ToString())]);

        return Outcome.Success();
    }

    private Outcome<CapabilityGrant> DenyCancelledRequest(
        AuditPrincipal principal,
        string consumerId,
        string capabilityTypeName)
    {
        _auditChannel.RecordDenial(
            principal,
            consumerId,
            capabilityTypeName,
            "CANCELLED",
            "Operation was cancelled");
        return Outcome<CapabilityGrant>.Failure();
    }

    private Outcome DenyCancelledRelease(AuditPrincipal principal)
    {
        _auditChannel.RecordDenial(
            principal,
            string.Empty,
            string.Empty,
            "CANCELLED",
            "Operation was cancelled");
        return Outcome.Failure();
    }

    private AuditPrincipal CapturePrincipal()
    {
        var authorization = _contextAccessor.Current?.Authorization;
        if (authorization is null)
        {
            return AuditPrincipal.Anonymous;
        }

        return new AuditPrincipal
        {
            UserId = authorization.UserId,
            TenantId = authorization.TenantId,
            SessionId = authorization.SessionId,
            IsAuthenticated = authorization.IsVerified
        };
    }

    private Dictionary<string, object> BuildConstraints(CapabilityRequest request)
    {
        var constraints = new Dictionary<string, object>(request.Constraints)
        {
            ["ConsumerId"] = request.ConsumerId
        };

        var authorization = _contextAccessor.Current?.Authorization;
        if (authorization is not null)
        {
            if (!string.IsNullOrEmpty(authorization.UserId))
            {
                constraints["UserId"] = authorization.UserId!;
            }

            if (!string.IsNullOrEmpty(authorization.TenantId))
            {
                constraints["TenantId"] = authorization.TenantId!;
            }

            foreach (var claim in authorization.Claims)
            {
                constraints[$"claim:{claim.Key}"] = claim.Value;
            }
        }

        return constraints;
    }


    private sealed class NoticeSubscription : IDisposable
    {
        private readonly Action _unsubscribe;
        private bool _disposed;

        public NoticeSubscription(Action unsubscribe)
        {
            _unsubscribe = unsubscribe;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _unsubscribe();
            _disposed = true;
        }
    }
}

public enum AttacheState
{
    Created,
    Starting,
    Running,
    Stopping,
    Stopped,
    Failed
}
