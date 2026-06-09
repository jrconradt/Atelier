using Atelier.Framework.Primitives;
using Atelier.Framework.Infrastructure;
using Atelier.Framework.Observability;
using Atelier.Framework.Infrastructure.Operation;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;
using Atelier.Framework.StateMachine.Interfaces;
using Atelier.Framework.StateMachine.Service;
using Microsoft.Extensions.Hosting;
using Atelier.Framework.Attributes;

namespace Atelier.Framework.StateMachine.Services;

[Infrastructure(InfrastructureLifetime.Scoped)]

public partial class StateMachineOrchestrator : IAtelier, IStateMachineManager, IHostedService
{
    [Requisite] protected readonly IStateMachineFactory _factory = null!;
    [Requisite] protected readonly IStateMachineTransitionService _transitionService = null!;
    [Requisite] protected readonly IStateMachineRegistryService _registryService = null!;
    [Requisite] protected readonly IStateMachineRestoreService _restoreService = null!;
    [Requisite] protected readonly IStateMachineMonitoringService _monitoringService = null!;
    [Requisite] protected readonly IHostApplicationLifetime _lifetime = null!;

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
        return await _factory.CreateStateMachineAsync<T>(instanceId, config, cancellationToken).ConfigureAwait(false);
    }

    [Operation("DestroyStateMachine")]
    public async Task<Outcome> DestroyStateMachineAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        if (instanceId is null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Instance ID was null")]);
            return Outcome.Failure();
        }
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome.Failure();
        }
        return await _factory.DestroyStateMachineAsync(instanceId, cancellationToken).ConfigureAwait(false);
    }

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
        return await _registryService.GetStateMachineAsync<T>(instanceId, cancellationToken).ConfigureAwait(false);
    }

    [Operation("TransitionStateMachine")]
    public async Task<Outcome> TransitionStateMachineAsync(
        string instanceId,
        string transitionName,
        CancellationToken cancellationToken = default)
    {
        if (instanceId is null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Instance ID was null")]);
            return Outcome.Failure();
        }
        if (transitionName is null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Transition name was null"), ("InstanceId", instanceId)]);
            return Outcome.Failure();
        }
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome.Failure();
        }
        return await _transitionService.TransitionStateMachineAsync(instanceId, transitionName, cancellationToken).ConfigureAwait(false);
    }

    [Operation("GetAllStateMachines")]
    public async Task<Outcome<IEnumerable<StateMachineInfo>>> GetAllStateMachinesAsync(
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome<IEnumerable<StateMachineInfo>>.Failure();
        }
        return await _registryService.GetAllStateMachinesAsync(cancellationToken).ConfigureAwait(false);
    }

    [Operation("RestoreStateMachine")]
    public async Task<Outcome> RestoreStateMachineAsync(
        string instanceId,
        StateMachineSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        if (instanceId is null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Instance ID was null")]);
            return Outcome.Failure();
        }
        if (snapshot is null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Snapshot was null"), ("InstanceId", instanceId)]);
            return Outcome.Failure();
        }
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome.Failure();
        }
        return await _restoreService.RestoreStateMachineAsync(instanceId, snapshot, cancellationToken).ConfigureAwait(false);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Observe(LogLevel.Information, message: "Starting state machine orchestrator");

        var restoreResult = await _restoreService.RestoreAllPersistedMachinesAsync(cancellationToken).ConfigureAwait(false);
        if (!restoreResult.IsSuccess)
        {
            Environment.ExitCode = 1;
            Observe(
                LogLevel.Error,
                message: "Failed to restore persisted machines");
            _lifetime.StopApplication();
            return;
        }

        var monitoringResult = await _monitoringService.StartMonitoringAsync(cancellationToken).ConfigureAwait(false);
        if (!monitoringResult.IsSuccess)
        {
            Environment.ExitCode = 1;
            Observe(
                LogLevel.Error,
                message: "Failed to start monitoring");
            _lifetime.StopApplication();
            return;
        }

        Observe(LogLevel.Information, message: "State machine orchestrator started successfully");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Observe(LogLevel.Information, message: "Stopping state machine orchestrator");

        await _monitoringService.StopMonitoringAsync(cancellationToken).ConfigureAwait(false);

        var allMachines = await _registryService.GetAllStateMachinesAsync(cancellationToken).ConfigureAwait(false);
        if (allMachines.IsSuccess && allMachines.Data != null)
        {
            foreach (var machine in allMachines.Data)
            {
                try
                {
                    await _monitoringService.CreateSnapshotAsync(machine.InstanceId, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Observe(
                        LogLevel.Error,
                        ex,
                        "Error saving final snapshot during shutdown",
                        values: [("InstanceId", machine.InstanceId)]);
                }
            }
        }

        Observe(LogLevel.Information, message: "State machine orchestrator stopped");
    }
}
