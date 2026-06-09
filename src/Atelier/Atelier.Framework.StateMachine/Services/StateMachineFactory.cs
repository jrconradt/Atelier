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

public partial class StateMachineFactory : IAtelier, IStateMachineFactory
{
    [Requisite] protected readonly IStateMachineRegistryService _registryService = null!;
    [Requisite(Required = false)] protected readonly IStateMachineMonitor? _monitor;

    [Operation("CreateStateMachine")]
    public async Task<Outcome<T>> CreateStateMachineAsync<T>(
        string instanceId,
        StateMachineConfiguration config,
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
        if (config is null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Configuration was null"), ("InstanceId", instanceId)]);
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
                values: [("Reason", "Instance ID was null or empty")]);
            return Outcome<T>.Failure();
        }

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "StateMachine", instanceId);

        var existingMachine = await _registryService.GetStateMachineAsync<T>(instanceId, cancellationToken).ConfigureAwait(false);
        if (existingMachine.IsSuccess)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "State machine already exists"), ("InstanceId", instanceId)]);
            return Outcome<T>.Failure();
        }

        var instance = new StateMachineInstance<T>().Configure(instanceId);
        var result = await instance.InitializeAsync(cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            Observe(
                LogLevel.Error,
                null,
                values: [("Reason", "State machine initialization failed"), ("InstanceId", instanceId)]);
            return Outcome<T>.Failure();
        }

        var registerResult = await _registryService.RegisterStateMachineAsync(instanceId, instance, cancellationToken).ConfigureAwait(false);
        if (!registerResult.IsSuccess)
        {
            await instance.DisposeAsync().ConfigureAwait(false);
            Observe(
                LogLevel.Error,
                null,
                values: [("Reason", "Failed to register state machine"), ("InstanceId", instanceId)]);
            return Outcome<T>.Failure();
        }

        Observe(
            LogLevel.Information,
            null, values: [("Message", "Created state machine"), ("InstanceId", instanceId), ("Type", typeof(T).Name)]);

        return Outcome<T>.Success(instance.StateMachine);
    }

    [Operation("DestroyStateMachine")]
    public async Task<Outcome> DestroyStateMachineAsync(
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

        var unregisterResult = await _registryService.UnregisterStateMachineAsync(instanceId, cancellationToken).ConfigureAwait(false);
        if (!unregisterResult.IsSuccess)
        {
            Observe(
                LogLevel.Warning,
                null, values: [("Reason", "Failed to unregister state machine during destruction"), ("InstanceId", instanceId)]);
            return Outcome.Failure();
        }

        Observe(
            LogLevel.Information,
            null, values: [("Message", "Destroyed state machine"), ("InstanceId", instanceId)]);

        return Outcome.Success();
    }
}
