using Atelier.Framework.Primitives;
using Atelier.Framework.Infrastructure;
using System.Collections.Concurrent;
using Atelier.Framework.Observability;
using Atelier.Framework.Infrastructure.Operation;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;
using Atelier.Framework.StateMachine.Interfaces;
using Atelier.Framework.StateMachine.Service;
using Atelier.Framework.Attributes;

namespace Atelier.Framework.StateMachine.Services;

[Infrastructure(InfrastructureLifetime.Singleton)]

public partial class StateMachineRegistryService : IAtelier, IStateMachineRegistryService, IAsyncDisposable
{
    [Requisite(Required = false)] protected readonly IStateMachineRegistry? _externalRegistry = null!;

    private readonly ConcurrentDictionary<string, IStateMachineInstance> _instances = new();

    [Operation("GetStateMachine")]
    public async Task<Outcome<T>> GetStateMachineAsync<T>(
        string instanceId,
        CancellationToken cancellationToken = default) where T : class, IStateMachine
    {
        if (instanceId is null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Instance ID was null")]);
            return Outcome<T>.Failure();
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome<T>.Failure();
        }

        if (string.IsNullOrWhiteSpace(instanceId))
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Instance ID was empty")]);
            return Outcome<T>.Failure();
        }

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "StateMachine", instanceId);

        if (!_instances.TryGetValue(instanceId, out var instance))
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "State machine not found"), ("InstanceId", instanceId)]);
            return Outcome<T>.Failure();
        }

        if (instance is StateMachineInstance<T> typedInstance)
        {
            if (typedInstance.StateMachine is T stateMachine)
            {
                return Outcome<T>.Success(stateMachine);
            }
        }

        Observe(
            LogLevel.Warning,
            null,
            values: [("Reason", "State machine type mismatch"), ("InstanceId", instanceId), ("RequestedType", typeof(T).Name)]);
        return Outcome<T>.Failure();
    }

    [Operation("GetAllStateMachines")]
    public async Task<Outcome<IEnumerable<StateMachineInfo>>> GetAllStateMachinesAsync(
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome<IEnumerable<StateMachineInfo>>.Failure();
        }

        var stateMachineInfos = _instances.Values.Select(instance => new StateMachineInfo
        {
            InstanceId = instance.InstanceId,
            Type = instance.Type,
            CurrentState = instance.CurrentState,
            IsHealthy = instance.IsHealthy,
            LastTransition = instance.LastTransition,
            CreatedAt = instance.CreatedAt
        });

        return Outcome<IEnumerable<StateMachineInfo>>.Success(stateMachineInfos);
    }

    [Operation("RegisterStateMachine")]
    public async Task<Outcome> RegisterStateMachineAsync(
        string instanceId,
        IStateMachineInstance instance,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome.Failure();
        }

        if (string.IsNullOrWhiteSpace(instanceId))
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Instance ID was null or empty")]);
            return Outcome.Failure();
        }

        if (instance == null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Instance was null"), ("InstanceId", instanceId)]);
            return Outcome.Failure();
        }

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "StateMachine", instanceId);

        if (!_instances.TryAdd(instanceId, instance))
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "State machine already registered"), ("InstanceId", instanceId)]);
            return Outcome.Failure();
        }

        _externalRegistry?.Register(instanceId, instance);

        Observe(
            LogLevel.Information,
            null, values: [("Message", "Registered state machine"), ("InstanceId", instanceId), ("Type", instance.Type)]);

        return Outcome.Success();
    }

    [Operation("UnregisterStateMachine")]
    public async Task<Outcome> UnregisterStateMachineAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome.Failure();
        }

        if (string.IsNullOrWhiteSpace(instanceId))
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Instance ID was null or empty")]);
            return Outcome.Failure();
        }

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "StateMachine", instanceId);

        if (!_instances.TryRemove(instanceId, out var instance))
        {
            Observe(
                LogLevel.Information,
                null, values: [("Message", "Unregister of absent state machine treated as success"), ("InstanceId", instanceId)]);
            return Outcome.Success();
        }

        try
        {
            await instance.DisposeAsync().ConfigureAwait(false);
            _externalRegistry?.Unregister(instanceId);

            Observe(
                LogLevel.Information,
                null, values: [("Message", "Unregistered state machine"), ("InstanceId", instanceId)]);

            return Outcome.Success();
        }
        catch (Exception ex)
        {
            Observe(
                LogLevel.Error,
                ex, values: [("Reason", "Error disposing state machine during unregistration"), ("InstanceId", instanceId)]);

            return Outcome.Failure();
        }
    }

    public IStateMachineInstance? GetInstance(string instanceId)
    {
        ArgumentNullException.ThrowIfNull(instanceId);
        return _instances.TryGetValue(instanceId, out var instance) ? instance : null;
    }

    public IEnumerable<IStateMachineInstance> GetAllInstances()
    {
        return _instances.Values;
    }

    public async ValueTask DisposeAsync()
    {
        var instanceIds = _instances.Keys.ToList();

        foreach (var instanceId in instanceIds)
        {
            if (_instances.TryRemove(instanceId, out var instance))
            {
                try
                {
                    await instance.DisposeAsync().ConfigureAwait(false);
                    _externalRegistry?.Unregister(instanceId);
                }
                catch (Exception ex)
                {
                    Observe(
                        LogLevel.Error,
                        ex, values: [("Message", "Error disposing state machine during registry shutdown"), ("InstanceId", instanceId)]);
                }
            }
        }
    }
}
