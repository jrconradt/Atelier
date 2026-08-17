using Atelier.Framework.Primitives;
using System.Collections.Concurrent;
using Atelier.Framework.Context;
using Atelier.Framework.Context.Extensions;
using Atelier.Framework.Infrastructure;
using Atelier.Framework.Attributes;
using Atelier.Framework.Network;
using Atelier.Framework.Observability;
using Atelier.Framework.Infrastructure.Operation;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;
using Atelier.Framework.Host.Execution;

namespace Atelier.Framework.Offering.Requisition;

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class OfferingRequisitionService : IAtelier, IOfferingRequisitionService
{
    [Requisite] protected readonly IOfferingManager _offeringManager = null!;
    [Requisite] protected readonly IOfferingProvider _offeringProvider = null!;
    [Requisite] protected readonly IOfferingResourceMonitor _resourceMonitor = null!;
    [Requisite] protected readonly Authorization.IRequisitionAuthorizer _authorizer = null!;
    private const string AdministratorRole = "RequisitionAdministrator";
    private const int MaxRequisitions = 10_000;
    private readonly ConcurrentDictionary<string, RequisitionTracker> _requisitions = new();

    [Operation("RequisitionOfferingAsync")]
    public async Task<Outcome<OfferingRequisitionResult>> RequisitionOfferingAsync<T>(
        OfferingRequisitionRequest request,
        CancellationToken cancellationToken = default) where T : class
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome<OfferingRequisitionResult>.Failure();
        }

        if (request is null)
        {
            Observe(LogLevel.Warning, values: [("Reason", "Requisition request was null")]);
            return Outcome<OfferingRequisitionResult>.Failure();
        }

        request.OfferingType = typeof(T);

        return await RequisitionOfferingInternalAsync(
            request,
            cancellationToken).ConfigureAwait(false);
    }

    [Operation("RequisitionOfferingAsync")]
    public async Task<Outcome<OfferingRequisitionResult>> RequisitionOfferingAsync(
        OfferingRequisitionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome<OfferingRequisitionResult>.Failure();
        }

        if (request is null)
        {
            Observe(LogLevel.Warning, values: [("Reason", "Requisition request was null")]);
            return Outcome<OfferingRequisitionResult>.Failure();
        }

        return await RequisitionOfferingInternalAsync(
            request,
            cancellationToken).ConfigureAwait(false);
    }

    [Operation("ReleaseRequisition")]
    public async Task<Outcome> ReleaseRequisitionAsync(
        string requisitionId,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome.Failure();
        }

        if (requisitionId is null)
        {
            Observe(LogLevel.Warning, values: [("Reason", "Requisition ID was null")]);
            return Outcome.Failure();
        }


        if (!_requisitions.TryGetValue(
            requisitionId,
            out var tracker))
        {
            Observe(LogLevel.Information, values: [("Message", "Release of absent requisition treated as success"), ("RequisitionId", requisitionId)]);
            return Outcome.Success();
        }

        if (!CallerOwnsRequisition(tracker))
        {
            Observe(LogLevel.Warning, values: [("Reason", "Caller is not authorized to release requisition"), ("RequisitionId", requisitionId), ("RequesterId", tracker.RequesterId), ("CallerId", AmbientContext.CurrentUserId ?? string.Empty)]);
            return Outcome.Failure();
        }

        if (tracker.ReleaseReference() > 0)
        {
            return Outcome.Success();
        }

        _requisitions.TryRemove(
            new KeyValuePair<string, RequisitionTracker>(requisitionId, tracker));

        await _offeringManager.StopOfferingAsync(
            tracker.InstanceId,
            cancellationToken).ConfigureAwait(false);

        Observe(LogLevel.Information, values: [("RequisitionId", requisitionId), ("OfferingType", tracker.OfferingType.Name), ("InstanceId", tracker.InstanceId)]);

        return Outcome.Success();
    }

    [Operation("GetRequisitionInfo")]
    public async Task<Outcome<RequisitionInfo>> GetRequisitionInfoAsync(
        string requisitionId,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome<RequisitionInfo>.Failure();
        }

        if (requisitionId is null)
        {
            Observe(LogLevel.Warning, values: [("Reason", "Requisition ID was null")]);
            return Outcome<RequisitionInfo>.Failure();
        }


        if (!_requisitions.TryGetValue(
            requisitionId,
            out var tracker))
        {
            Observe(LogLevel.Warning, values: [("Reason", "Requisition not found"), ("RequisitionId", requisitionId)]);
            return Outcome<RequisitionInfo>.Failure();
        }

        if (!CallerOwnsRequisition(tracker))
        {
            Observe(LogLevel.Warning, values: [("Reason", "Caller is not authorized to inspect requisition"), ("RequisitionId", requisitionId)]);
            return Outcome<RequisitionInfo>.Failure();
        }

        var info = new RequisitionInfo
        {
            RequisitionId = tracker.RequisitionId,
            InstanceId = tracker.InstanceId,
            RequesterId = tracker.RequesterId,
            RequesterType = tracker.RequesterType,
            OfferingType = tracker.OfferingType,
            Status = RequisitionStatus.Approved,
            RequisitionedAt = tracker.RequisitionedAt,
            ReleasedAt = tracker.ReleasedAt,
            IsShared = tracker.IsShared,
            ReferenceCount = tracker.ReferenceCount
        };

        return info;
    }

    [Operation("GetRequisitionsByRequester")]
    public async Task<Outcome<List<RequisitionInfo>>> GetRequisitionsByRequesterAsync(
        string requesterId,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome<List<RequisitionInfo>>.Failure();
        }

        if (requesterId is null)
        {
            Observe(LogLevel.Warning, values: [("Reason", "Requester ID was null")]);
            return Outcome<List<RequisitionInfo>>.Failure();
        }

        var callerId = AmbientContext.CurrentUserId;
        if (!CallerIsAdministrator($"requester:{requesterId}")
            && !string.Equals(callerId, requesterId, StringComparison.Ordinal))
        {
            Observe(LogLevel.Warning, values: [("Reason", "Caller may only query requisitions for their own requester identity"), ("RequesterId", requesterId)]);
            return Outcome<List<RequisitionInfo>>.Failure();
        }

        return _requisitions.Values
            .Where(t => t.RequesterId == requesterId)
            .Select(t => new RequisitionInfo
            {
                RequisitionId = t.RequisitionId,
                InstanceId = t.InstanceId,
                RequesterId = t.RequesterId,
                RequesterType = t.RequesterType,
                OfferingType = t.OfferingType,
                Status = RequisitionStatus.Approved,
                RequisitionedAt = t.RequisitionedAt,
                ReleasedAt = t.ReleasedAt,
                IsShared = t.IsShared,
                ReferenceCount = t.ReferenceCount
            })
            .ToList();
    }

    private async Task<Outcome<OfferingRequisitionResult>> RequisitionOfferingInternalAsync(
        OfferingRequisitionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requisitionId = Guid.NewGuid().ToString();
        var requesterPrincipalId = AmbientContext.CurrentUserId ?? string.Empty;

        if (request.AllowSharedInstance)
        {
            var existingResult = AcquireSharedInstance(request, requesterPrincipalId);
            if (existingResult != null)
            {
                return existingResult;
            }
        }

        if (_requisitions.Count >= MaxRequisitions)
        {
            Observe(LogLevel.Warning, values: [("Reason", "Active requisition ceiling reached"), ("Ceiling", MaxRequisitions), ("OfferingType", request.OfferingType.Name)]);
            return Outcome<OfferingRequisitionResult>.Failure();
        }

        var placementResult = DetermineZonePlacement(request);
        if (!placementResult.IsSuccess || placementResult.Data == null)
        {
            return Outcome<OfferingRequisitionResult>.Failure();
        }

        var targetZone = placementResult.Data!;

        if (request.ResourceRequirements != null)
        {
            var approvalResult = ApproveResourceRequest(request.ResourceRequirements);
            if (!approvalResult.IsSuccess)
            {
                return Outcome<OfferingRequisitionResult>.Failure();
            }
        }

        var startOptions = new OfferingStartOptions
        {
            ExecutionMode = request.PreferredExecutionMode,
            ResourceLimits = request.ResourceRequirements,
            EnvironmentVariables = request.Configuration,
            Metadata = request.Metadata,
            AutoRegisterDiscovery = true
        };

        if (request.Backing is not null)
        {
            startOptions.DockerImage = request.Backing.ImageName;
            startOptions.DockerContainerName = request.Backing.ContainerNamePrefix;
            startOptions.ExposedPorts = request.Backing.ExposedPorts;
            startOptions.DockerLabels = request.Backing.Labels;
            startOptions.ResourceLimits = request.Backing.ResourceLimits ?? request.ResourceRequirements;

            var mergedEnvironment = new Dictionary<string, string>(request.Configuration);
            foreach (var variable in request.Backing.EnvironmentVariables)
            {
                mergedEnvironment[variable.Key] = variable.Value;
            }

            startOptions.EnvironmentVariables = mergedEnvironment;
        }

        var startResult = await _offeringManager.StartOfferingAsync(
            request.OfferingType,
            startOptions,
            cancellationToken).ConfigureAwait(false);

        if (!startResult.IsSuccess || startResult.Data == null)
        {
            return Outcome<OfferingRequisitionResult>.Failure();
        }

        var instanceId = startResult.Data;
        var descriptor = _offeringManager.GetOfferingDescriptor(instanceId);

        var requisitionResult = new OfferingRequisitionResult
        {
            InstanceId = instanceId,
            RequisitionId = requisitionId,
            OfferingType = request.OfferingType,
            ExecutionMode = request.PreferredExecutionMode,
            PlacedZone = targetZone,
            IsSharedInstance = request.AllowSharedInstance,
            NetworkAddress = descriptor?.NetworkAddress,
            NetworkPort = descriptor?.NetworkPort,
            ProcessId = descriptor?.ProcessId,
            AllocatedResources = request.ResourceRequirements,
            Metadata = request.Metadata,
            RequisitionedAt = DateTime.UtcNow,
            Status = RequisitionStatus.Approved
        };

        _requisitions[requisitionId] = new RequisitionTracker
        {
            RequisitionId = requisitionId,
            InstanceId = instanceId,
            RequesterId = requesterPrincipalId,
            RequesterType = request.RequesterType,
            RequesterTenantId = AmbientContext.CurrentTenantId ?? string.Empty,
            OfferingType = request.OfferingType,
            PlacedZone = targetZone,
            IsShared = request.AllowSharedInstance,
            RequisitionedAt = DateTime.UtcNow,
            ReferenceCount = 1
        };

        Observe(LogLevel.Information, values: [("OfferingType", request.OfferingType.Name), ("InstanceId", instanceId.ToString()), ("PlacedZone", targetZone.ToString()), ("RequesterId", requesterPrincipalId)]);

        return requisitionResult;
    }

    private OfferingRequisitionResult? AcquireSharedInstance(
        OfferingRequisitionRequest request,
        string requesterPrincipalId)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(requesterPrincipalId);

        var callerTenantId = AmbientContext.CurrentTenantId ?? string.Empty;
        if (string.IsNullOrEmpty(callerTenantId))
        {
            return null;
        }

        var shared = _requisitions.Values
            .FirstOrDefault(t =>
                t.OfferingType == request.OfferingType
                && t.IsShared
                && string.Equals(t.RequesterId, requesterPrincipalId, StringComparison.Ordinal)
                && t.RequesterType == request.RequesterType
                && string.Equals(t.RequesterTenantId, callerTenantId, StringComparison.Ordinal)
                && (request.RequesterZone == null || t.PlacedZone == request.RequesterZone));

        if (shared != null)
        {
            if (!shared.TryAcquireReference())
            {
                return null;
            }

            return new OfferingRequisitionResult
            {
                InstanceId = shared.InstanceId,
                RequisitionId = shared.RequisitionId,
                OfferingType = shared.OfferingType,
                PlacedZone = shared.PlacedZone,
                IsSharedInstance = true,
                RequisitionedAt = shared.RequisitionedAt,
                Status = RequisitionStatus.Approved
            };
        }

        return null;
    }

    private bool CallerOwnsRequisition(RequisitionTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(tracker);

        if (CallerIsAdministrator(tracker.RequisitionId))
        {
            return true;
        }

        var callerId = AmbientContext.CurrentUserId;
        if (string.IsNullOrEmpty(callerId))
        {
            return false;
        }

        return string.Equals(callerId, tracker.RequesterId, StringComparison.Ordinal);
    }

    private bool CallerIsAdministrator(string resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return _authorizer.IsAuthorizedForRole(AdministratorRole, resource);
    }

    private Outcome<Type?> DetermineZonePlacement(OfferingRequisitionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        switch (request.PlacementStrategy)
        {
            case ZonePlacementStrategy.SameZone:
            {
                if (request.RequesterZone == null)
                {
                    Observe(LogLevel.Warning, values: [("Reason", "SameZone strategy requires RequesterZone to be specified"), ("OfferingType", request.OfferingType.Name)]);
                    return Outcome<Type?>.Failure();
                }
                return Outcome<Type?>.Success(request.RequesterZone);
            }

            case ZonePlacementStrategy.RequireSpecificZone:
            {
                if (request.TargetZone == null)
                {
                    Observe(LogLevel.Warning, values: [("Reason", "RequireSpecificZone strategy requires TargetZone to be specified"), ("OfferingType", request.OfferingType.Name)]);
                    return Outcome<Type?>.Failure();
                }
                return Outcome<Type?>.Success(request.TargetZone);
            }

            case ZonePlacementStrategy.AllowCrossZone:
            {
                return Outcome<Type?>.Success(
                    request.TargetZone ?? request.RequesterZone ?? typeof(Atelier.Framework.Primitives.Application));
            }

            case ZonePlacementStrategy.AutoDetect:
            {
                var detectedZone = DetectOptimalZone(request);
                return Outcome<Type?>.Success(detectedZone);
            }

            default:
            {
                return Outcome<Type?>.Success(typeof(Atelier.Framework.Primitives.Application));
            }
        }
    }

    private Type DetectOptimalZone(OfferingRequisitionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var zoneAttribute = request.OfferingType
            .GetCustomAttributes(typeof(NetworkZoneAttribute), false)
            .FirstOrDefault() as NetworkZoneAttribute;

        if (zoneAttribute is not null)
        {
            return zoneAttribute.Zone;
        }

        return request.RequesterZone ?? typeof(Atelier.Framework.Primitives.Application);
    }

    private Outcome ApproveResourceRequest(ResourceAllocation resourceRequirements)
    {
        ArgumentNullException.ThrowIfNull(resourceRequirements);

        if (!_resourceMonitor.IsWithinLimits(resourceRequirements))
        {
            var violation = _resourceMonitor.DetectViolation(resourceRequirements);
            Observe(LogLevel.Warning, values: [("Reason", "Resource limits exceeded"), ("Violation", violation?.Message ?? "Resource limits exceeded")]);
            return Outcome.Failure();
        }

        return Outcome.Success();
    }

    public IOfferingHandle<T> CreateHandle<T>(OfferingRequisitionResult result) where T : class
    {
        ArgumentNullException.ThrowIfNull(result);

        return new OfferingHandle<T>(
            result,
            _offeringManager,
            _offeringProvider);
    }

    private class RequisitionTracker
    {
        public string RequisitionId { get; set; } = string.Empty;
        public string InstanceId { get; set; } = string.Empty;
        public string RequesterId { get; set; } = string.Empty;
        public Type RequesterType { get; set; } = null!;
        public string RequesterTenantId { get; set; } = string.Empty;
        public Type OfferingType { get; set; } = null!;
        public Type PlacedZone { get; set; } = null!;
        public bool IsShared { get; set; }
        public DateTime RequisitionedAt { get; set; }
        public DateTime? ReleasedAt { get; set; }

        private int _referenceCount;

        public int ReferenceCount
        {
            get => Volatile.Read(ref _referenceCount);
            set => Volatile.Write(ref _referenceCount, value);
        }

        public bool TryAcquireReference()
        {
            while (true)
            {
                var current = Volatile.Read(ref _referenceCount);
                if (current <= 0)
                {
                    return false;
                }

                if (Interlocked.CompareExchange(ref _referenceCount, current + 1, current) == current)
                {
                    return true;
                }
            }
        }

        public int ReleaseReference()
        {
            return Interlocked.Decrement(ref _referenceCount);
        }
    }
}
