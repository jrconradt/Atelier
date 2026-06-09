using Atelier.Framework.Primitives;
using System.Collections.Concurrent;
using System.Reflection;
using Atelier.Framework.Infrastructure;
using Atelier.Framework.Observability;
using Atelier.Framework.Infrastructure.Operation;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;
using Atelier.Framework.StateMachine.Interfaces;
using Atelier.Framework.StateMachine.Service;
using Atelier.Framework.Attributes;

namespace Atelier.Framework.StateMachine.Services;

[Infrastructure(InfrastructureLifetime.Scoped)]

public partial class StateMachineRestoreService : IAtelier, IStateMachineRestoreService
{
    [Requisite] protected readonly IStateMachineRegistryService _registryService = null!;
    [Requisite] protected readonly IStateMachinePersistence _persistence = null!;
    [Requisite(Required = false)] protected readonly IStateMachineMonitor? _monitor;

    private static readonly ConcurrentDictionary<Type, Func<StateMachineSnapshot, CancellationToken, Task<Outcome<IStateMachineInstance>>>> RestoreFactories = new();

    private static readonly MethodInfo RestoreTypedMethod = typeof(StateMachineRestoreService)
        .GetMethod(nameof(RestoreTypedAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static async Task<Outcome<IStateMachineInstance>> RestoreTypedAsync<T>(
        StateMachineSnapshot snapshot,
        CancellationToken cancellationToken) where T : class, IStateMachine
    {
        var result = await StateMachineInstance<T>.FromSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess
            && result.Data is IStateMachineInstance instance)
        {
            return Outcome<IStateMachineInstance>.Success(instance);
        }

        return Outcome<IStateMachineInstance>.Failure();
    }

    [Operation("RestoreStateMachine")]
    public async Task<Outcome> RestoreStateMachineAsync(
        string instanceId,
        StateMachineSnapshot snapshot,
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

        if (snapshot == null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Snapshot was null"), ("InstanceId", instanceId)]);
            return Outcome.Failure();
        }

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "StateMachine", instanceId);

        var existingMachine = await _registryService.GetStateMachineAsync<IStateMachine>(instanceId, cancellationToken).ConfigureAwait(false);
        if (existingMachine.IsSuccess)
        {
            Observe(
                LogLevel.Information,
                null, values: [("Message", "Restore of already-present state machine treated as success"), ("InstanceId", instanceId)]);
            return Outcome.Success();
        }

        var instanceResult = await RestoreInstanceFromSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);
        if (!instanceResult.IsSuccess)
        {
            Observe(
                LogLevel.Error,
                null,
                values: [("Reason", "Failed to restore instance from snapshot"), ("InstanceId", instanceId)]);
            return Outcome.Failure();
        }

        var instance = instanceResult.Data!;
        var registerResult = await _registryService.RegisterStateMachineAsync(instanceId, instance, cancellationToken).ConfigureAwait(false);

        if (!registerResult.IsSuccess)
        {
            await instance.DisposeAsync().ConfigureAwait(false);
            Observe(
                LogLevel.Error,
                null,
                values: [("Reason", "Failed to register restored state machine"), ("InstanceId", instanceId)]);
            return Outcome.Failure();
        }

        Observe(
            LogLevel.Information,
            null, values: [("Message", "Restored state machine from snapshot"), ("InstanceId", instanceId), ("SnapshotTimestamp", snapshot.SnapshotAt)]);

        return Outcome.Success();
    }

    [Operation("RestoreAllPersistedMachines")]
    public async Task<Outcome> RestoreAllPersistedMachinesAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome.Failure();
        }

        var snapshotsResult = await _persistence.GetAllSnapshotsAsync(cancellationToken).ConfigureAwait(false);

        if (!snapshotsResult.IsSuccess)
        {
            Observe(
                LogLevel.Error,
                null,
                values: [("Reason", "Failed to get snapshots")]);
            return Outcome.Failure();
        }

        if (snapshotsResult.Data == null)
        {
            Observe(
                LogLevel.Information,
                null, values: [("Message", "No persisted state machines found to restore")]);

            return Outcome.Success();
        }

        var successCount = 0;
        var errorCount = 0;

        foreach (var snapshot in snapshotsResult.Data)
        {
            var restoreResult = await RestoreStateMachineAsync(snapshot.InstanceId, snapshot, cancellationToken).ConfigureAwait(false);

            if (restoreResult.IsSuccess)
            {
                successCount++;
            }
            else
            {
                errorCount++;
                Observe(
                    LogLevel.Warning,
                    null,
                    values: [("Reason", "Failed to restore persisted state machine"), ("InstanceId", snapshot.InstanceId)]);
            }
        }

        Observe(
            LogLevel.Information,
            null, values: [("Message", "Completed restoring persisted state machines"), ("SuccessCount", successCount), ("ErrorCount", errorCount), ("TotalSnapshots", snapshotsResult.Data.Count())]);

        if (errorCount > 0)
        {
            return Outcome.Failure();
        }

        return Outcome.Success();
    }

    private async Task<Outcome<IStateMachineInstance>> RestoreInstanceFromSnapshotAsync(StateMachineSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (snapshot is null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Snapshot was null")]);
            return Outcome<IStateMachineInstance>.Failure();
        }

        var type = SafeTypeResolver.Resolve(snapshot.Type, typeof(IStateMachine));
        if (type == null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Snapshot type is not a permitted state machine type"), ("SnapshotType", snapshot.Type)]);
            return Outcome<IStateMachineInstance>.Failure();
        }

        var factory = RestoreFactories.GetOrAdd(type, BuildRestoreFactory);
        return await factory(snapshot, cancellationToken).ConfigureAwait(false);
    }

    private static Func<StateMachineSnapshot, CancellationToken, Task<Outcome<IStateMachineInstance>>> BuildRestoreFactory(Type stateMachineType)
    {
        return RestoreTypedMethod
            .MakeGenericMethod(stateMachineType)
            .CreateDelegate<Func<StateMachineSnapshot, CancellationToken, Task<Outcome<IStateMachineInstance>>>>();
    }
}
