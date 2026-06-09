using Atelier.Framework.Primitives;
using System.Collections.Concurrent;
using Atelier.Framework.Infrastructure;
using Atelier.Framework.Attributes;
using Atelier.Framework.Context;
using Atelier.Framework.Context.Extensions;
using Atelier.Framework.Offering.Discovery;
using Atelier.Framework.Infrastructure.Operation;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;
using Atelier.Framework.Host.Execution;

namespace Atelier.Framework.Offering;

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class OfferingManager : IOfferingManager, IAtelier, IAsyncDisposable
{
    [Requisite] protected readonly IExecutorFactory _executorFactory = null!;

    [Requisite] protected readonly IOfferingRegistry _offeringRegistry = null!;

    [Requisite] protected readonly IOfferingResourceMonitor _resourceMonitor = null!;

    [Requisite] protected readonly IContextAccessor _contextAccessor = null!;
    private readonly ConcurrentDictionary<string, Atelier.Framework.Host.Execution.HostExecutionContext> _activeOfferings = new();
    private readonly ConcurrentDictionary<string, byte> _capacityReservations = new();

    private const string SystemAnnouncerOwner = "system:offering-host";
    private const int MaxActiveOfferings = 10_000;

    private string ResolveAnnouncerIdentity()
    {
        var userId = _contextAccessor.GetCurrentUserId();
        return string.IsNullOrEmpty(userId)
            ? SystemAnnouncerOwner
            : userId;
    }

    private bool AnnounceOwned(OfferingAnnouncement announcement)
    {
        ArgumentNullException.ThrowIfNull(announcement);

        return _offeringRegistry.Announce(
            announcement,
            ResolveAnnouncerIdentity());
    }

    private bool RevokeOwned(string instanceId)
    {
        ArgumentNullException.ThrowIfNull(instanceId);

        return _offeringRegistry.Revoke(
            instanceId,
            ResolveAnnouncerIdentity());
    }

    public OfferingInstanceDescriptor? GetOfferingDescriptor(string instanceId)
    {
        ArgumentNullException.ThrowIfNull(instanceId);

        return _activeOfferings.TryGetValue(instanceId, out var context)
            ? MapToDescriptor(context)
            : null;
    }

    public IEnumerable<OfferingInstanceDescriptor> GetAllOfferings()
    {
        return _activeOfferings.Values.Select(MapToDescriptor).ToList();
    }

    public Task<Outcome<string>> StartOffering(Type offeringType, OfferingStartOptions options)
    {
        if (offeringType is null)
        {
            Observe(LogLevel.Warning, values: [("Reason", "Offering type was null")]);
            return Task.FromResult(Outcome<string>.Failure());
        }

        if (options is null)
        {
            Observe(LogLevel.Warning, values: [("Reason", "Offering start options were null")]);
            return Task.FromResult(Outcome<string>.Failure());
        }

        return StartOfferingCore(
            offeringType,
            options,
            CancellationToken.None);
    }

    [Operation("StartOfferingAsync")]
    public Task<Outcome<string>> StartOfferingAsync(Type offeringType,
                                                    OfferingStartOptions options,
                                                    CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Outcome<string>.Failure());
        }

        if (offeringType is null)
        {
            Observe(LogLevel.Warning, values: [("Reason", "Offering type was null")]);
            return Task.FromResult(Outcome<string>.Failure());
        }

        if (options is null)
        {
            Observe(LogLevel.Warning, values: [("Reason", "Offering start options were null")]);
            return Task.FromResult(Outcome<string>.Failure());
        }

        return StartOfferingCore(
            offeringType,
            options,
            cancellationToken);
    }

    private async Task<Outcome<string>> StartOfferingCore(Type offeringType, OfferingStartOptions options, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome<string>.Failure();
        }

        if (offeringType is null)
        {
            Observe(LogLevel.Warning, values: [("Reason", "Offering type was null")]);
            return Outcome<string>.Failure();
        }

        if (options is null)
        {
            Observe(LogLevel.Warning, values: [("Reason", "Offering start options were null")]);
            return Outcome<string>.Failure();
        }

        if (options.ResourceLimits != null
            && !_resourceMonitor.IsWithinLimits(options.ResourceLimits))
        {
            var violation = _resourceMonitor.DetectViolation(options.ResourceLimits);
            Observe(LogLevel.Warning, values: [("OfferingType", offeringType.FullName ?? offeringType.Name), ("Reason", "Resource limits exceeded"), ("Violation", violation?.Message ?? "Unknown violation")]);
            return Outcome<string>.Failure();
        }

        var executor = _executorFactory.GetExecutor(options.ExecutionMode);
        if (executor == null)
        {
            Observe(LogLevel.Error, values: [("OfferingType", offeringType.FullName ?? offeringType.Name), ("Reason", "No executor registered for execution mode"), ("ExecutionMode", options.ExecutionMode.ToString())]);
            return Outcome<string>.Failure();
        }

        var reservationId = Guid.NewGuid().ToString();
        _capacityReservations[reservationId] = 0;

        if (_activeOfferings.Count + _capacityReservations.Count > MaxActiveOfferings)
        {
            _capacityReservations.TryRemove(reservationId, out _);
            Observe(LogLevel.Warning, values: [("OfferingType", offeringType.FullName ?? offeringType.Name), ("Reason", "Active offering ceiling reached"), ("Ceiling", MaxActiveOfferings)]);
            return Outcome<string>.Failure();
        }

        var executionOptions = MapToExecutionOptions(options);
        Atelier.Framework.Host.Execution.HostExecutionContext? context;
        try
        {
            context = await executor.StartOfferingAsync(
                offeringType,
                executionOptions,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _capacityReservations.TryRemove(reservationId, out _);
            Observe(LogLevel.Error, ex, values: [("OfferingType", offeringType.FullName ?? offeringType.Name), ("Reason", "Executor failed to start offering")]);
            return Outcome<string>.Failure();
        }

        if (context == null)
        {
            _capacityReservations.TryRemove(reservationId, out _);
            Observe(LogLevel.Error, values: [("OfferingType", offeringType.FullName ?? offeringType.Name), ("Reason", "Executor returned no execution context")]);
            return Outcome<string>.Failure();
        }

        _activeOfferings[context.InstanceId] = context;
        _capacityReservations.TryRemove(reservationId, out _);

        if (options.AutoRegisterDiscovery
            && !string.IsNullOrEmpty(context.NetworkAddress)
            && context.NetworkPort.HasValue)
        {
            var announced = AnnounceOwned(new OfferingAnnouncement
            {
                InstanceId = context.InstanceId,
                OfferingTypeName = offeringType.FullName ?? offeringType.Name,
                NetworkAddress = context.NetworkAddress,
                NetworkPort = context.NetworkPort.Value,
                Metadata = options.Metadata
            });

            if (!announced)
            {
                Observe(LogLevel.Warning, values: [("InstanceId", context.InstanceId), ("OfferingType", offeringType.FullName ?? offeringType.Name), ("DiscoveryAnnounceRejected", true)]);
            }
        }

        return Outcome<string>.Success(context.InstanceId);
    }

    [Operation("StopOfferingAsync")]
    public async Task<Outcome> StopOfferingAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome.Failure();
        }

        if (instanceId is null)
        {
            Observe(LogLevel.Warning, values: [("Reason", "Instance ID was null")]);
            return Outcome.Failure();
        }

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "Offering", instanceId);

        if (!_activeOfferings.TryRemove(instanceId, out var context))
        {
            Observe(LogLevel.Information, values: [("Message", "Stop of absent offering treated as success"), ("InstanceId", instanceId)]);
            return Outcome.Success();
        }

        try
        {
            var executor = _executorFactory.GetExecutor(context.ExecutionMode);
            await executor.StopOfferingAsync(
                context,
                cancellationToken).ConfigureAwait(false);
            RevokeOwned(instanceId);
            return Outcome.Success();
        }
        catch (Exception ex)
        {
            Observe(LogLevel.Error, ex, values: [("Reason", "Failed to stop offering"), ("InstanceId", instanceId)]);
            return Outcome.Failure();
        }
        finally
        {
            _resourceMonitor.RemoveInstance(instanceId);
            context.CancellationTokenSource?.Cancel();
            context.CancellationTokenSource?.Dispose();
        }
    }

    public async Task<Outcome> StopOffering(string instanceId)
    {
        if (instanceId is null)
        {
            Observe(LogLevel.Warning, values: [("Reason", "Instance ID was null")]);
            return Outcome.Failure();
        }

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "Offering", instanceId);

        if (!_activeOfferings.TryRemove(instanceId, out var context))
        {
            Observe(LogLevel.Information, values: [("Message", "Stop of absent offering treated as success"), ("InstanceId", instanceId)]);
            return Outcome.Success();
        }

        var executor = _executorFactory.GetExecutor(context.ExecutionMode);
        await executor.StopOfferingAsync(
            context,
            CancellationToken.None).ConfigureAwait(false);
        RevokeOwned(instanceId);
        _resourceMonitor.RemoveInstance(instanceId);
        context.CancellationTokenSource?.Cancel();
        context.CancellationTokenSource?.Dispose();
        return Outcome.Success();
    }

    public IEnumerable<OfferingInstanceDescriptor> GetOfferingsByType(Type offeringType)
    {
        ArgumentNullException.ThrowIfNull(offeringType);

        return _activeOfferings.Values
            .Where(c => c.OfferingType == offeringType
                || c.OfferingTypeName == offeringType.FullName
                || c.OfferingTypeName == offeringType.Name)
            .Select(MapToDescriptor)
            .ToList();
    }

    public IEnumerable<OfferingAnnouncement> DiscoverNetworkOfferings(string? offeringTypeName = null)
    {
        if (string.IsNullOrEmpty(offeringTypeName))
        {
            return _offeringRegistry.GetAllAnnouncements();
        }

        return _offeringRegistry.GetAnnouncementsByOfferingType(offeringTypeName);
    }

    public Outcome UpdateOfferingHeartbeat(string instanceId)
    {
        if (instanceId is null)
        {
            Observe(LogLevel.Warning, values: [("Reason", "Instance ID was null")]);
            return Outcome.Failure();
        }

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "Offering", instanceId);

        if (!_activeOfferings.ContainsKey(instanceId))
        {
            Observe(LogLevel.Warning, values: [("Reason", "Offering instance not found"), ("InstanceId", instanceId)]);
            return Outcome.Failure();
        }

        _offeringRegistry.UpdateHeartbeat(instanceId);
        return Outcome.Success();
    }

    [Operation("CreateOfferingInstance")]
    public async Task<Outcome<OfferingInstanceResponse>> CreateOfferingInstance(CreateOfferingRequest input, OperationContext context, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome<OfferingInstanceResponse>.Failure();
        }

        if (input is null)
        {
            Observe(LogLevel.Warning, values: [("Reason", "Create request input was null")]);
            return Outcome<OfferingInstanceResponse>.Failure();
        }

        if (context is null)
        {
            Observe(LogLevel.Warning, values: [("Reason", "Operation context was null")]);
            return Outcome<OfferingInstanceResponse>.Failure();
        }

        if (string.IsNullOrEmpty(input.OfferingTypeName))
        {
            Observe(LogLevel.Warning, values: [("Reason", "Invalid offering type name")]);
            return Outcome<OfferingInstanceResponse>.Failure();
        }

        if (input.OfferingTypeName.Contains(','))
        {
            Observe(LogLevel.Warning, values: [("Reason", "Assembly-qualified offering type names are not permitted"), ("OfferingTypeName", input.OfferingTypeName)]);
            return Outcome<OfferingInstanceResponse>.Failure();
        }

        var offeringType = SafeTypeResolver.Resolve(input.OfferingTypeName, typeof(IOffering));
        if (offeringType == null)
        {
            Observe(LogLevel.Warning, values: [("Reason", "Offering type not found or does not implement IOffering"), ("OfferingTypeName", input.OfferingTypeName)]);
            return Outcome<OfferingInstanceResponse>.Failure();
        }

        var options = new OfferingStartOptions
        {
            ExecutionMode = input.ExecutionMode,
            TargetProcessId = input.TargetProcessId,
            NetworkAddress = input.NetworkAddress,
            NetworkPort = input.NetworkPort,
            AutoRegisterDiscovery = input.AutoRegisterDiscovery,
            EnvironmentVariables = input.EnvironmentVariables,
            Metadata = input.Metadata,
            ResourceLimits = input.ResourceLimits,
            SecretClaims = input.SecretClaims
        };

        var result = await StartOfferingAsync(offeringType, options, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Outcome<OfferingInstanceResponse>.Failure();
        }

        var descriptor = GetOfferingDescriptor(result.Data!);
        return Outcome<OfferingInstanceResponse>.Success(OfferingInstanceResponse.Success(descriptor!));
    }

    [Operation("DeleteOfferingInstance")]
    public async Task<Outcome> DeleteOfferingInstance(string input, OperationContext context, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome.Failure();
        }

        if (input is null)
        {
            Observe(LogLevel.Warning, values: [("Reason", "Delete request instance ID was null")]);
            return Outcome.Failure();
        }

        if (context is null)
        {
            Observe(LogLevel.Warning, values: [("Reason", "Operation context was null"), ("InstanceId", input)]);
            return Outcome.Failure();
        }

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "Offering", input);

        var result = await StopOfferingAsync(input, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            Observe(LogLevel.Error, values: [("Reason", "Failed to delete offering instance"), ("InstanceId", input)]);
            return Outcome.Failure();
        }

        return Outcome.Success();
    }

    private OfferingInstanceDescriptor MapToDescriptor(Atelier.Framework.Host.Execution.HostExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new OfferingInstanceDescriptor
        {
            InstanceId = context.InstanceId,
            OfferingType = context.OfferingType,
            OfferingTypeName = context.OfferingTypeName ?? string.Empty,
            ExecutionMode = context.ExecutionMode,
            State = (OfferingInstanceState)context.State,
            CreatedAt = context.CreatedAt,
            StartedAt = context.StartedAt,
            StoppedAt = context.StoppedAt,
            ProcessId = context.ProcessId,
            NetworkAddress = context.NetworkAddress,
            NetworkPort = context.NetworkPort,
            Metadata = context.Metadata ?? new Dictionary<string, string>(),
            ResourceAllocation = context.ResourceAllocation,
            FailureReason = context.FailureReason
        };
    }

    private ExecutionOptions MapToExecutionOptions(OfferingStartOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new ExecutionOptions
        {
            ExecutionMode = options.ExecutionMode,
            TargetProcessId = options.TargetProcessId,
            NetworkAddress = options.NetworkAddress,
            NetworkPort = options.NetworkPort,
            AutoRegisterDiscovery = options.AutoRegisterDiscovery,
            EnvironmentVariables = options.EnvironmentVariables ?? new Dictionary<string, string>(),
            Metadata = options.Metadata ?? new Dictionary<string, string>(),
            ResourceLimits = options.ResourceLimits,
            SecretClaims = options.SecretClaims ?? new List<string>(),
            DockerImage = options.DockerImage,
            DockerContainerName = options.DockerContainerName,
            DockerLabels = options.DockerLabels ?? new Dictionary<string, string>(),
            ExposedPorts = options.ExposedPorts ?? new List<int>(),
            AllowedImageRegistries = options.AllowedImageRegistries ?? new List<string>()
        };
    }

    public async ValueTask DisposeAsync()
    {
        var instances = _activeOfferings.Keys.ToList();

        foreach (var instanceId in instances)
        {
            var stopOutcome = await StopOfferingAsync(instanceId).ConfigureAwait(false);
            if (!stopOutcome.IsSuccess)
            {
                Observe(LogLevel.Warning, values: [("InstanceId", instanceId), ("Reason", "Failed to stop offering during registry shutdown")]);
            }
        }
    }
}
