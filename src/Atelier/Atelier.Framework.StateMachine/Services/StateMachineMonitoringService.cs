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

[Infrastructure(InfrastructureLifetime.Singleton)]

public partial class StateMachineMonitoringService : IAtelier, IStateMachineMonitoringService, IDisposable
{
    [Requisite] protected readonly IStateMachineRegistryService _registryService = null!;
    [Requisite] protected readonly IStateMachinePersistence _persistence = null!;
    [Requisite(Required = false)] protected readonly IStateMachineMonitor? _monitor;

    private readonly TimeSpan _monitoringInterval = TimeSpan.FromSeconds(30);
    private readonly MonitoringState _state = new();

    private sealed class MonitoringState
    {
        public PeriodicTimer? Timer;
        public Task Loop = Task.CompletedTask;
        public CancellationTokenSource? Stopping;
    }

    public Task<Outcome> StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Outcome.Failure());
        }

        if (_state.Stopping is not null)
        {
            return Task.FromResult(Outcome.Success());
        }

        _state.Stopping = new CancellationTokenSource();
        _state.Timer = new PeriodicTimer(_monitoringInterval);
        _state.Loop = RunMonitorLoopAsync(_state.Stopping.Token);

        Observe(
            LogLevel.Information,
            null, values: [("Message", "State machine monitoring started"), ("IntervalSeconds", _monitoringInterval.TotalSeconds)]);

        return Task.FromResult(Outcome.Success());
    }

    private async Task RunMonitorLoopAsync(CancellationToken cancellationToken)
    {
        var timer = _state.Timer;
        if (timer is null)
        {
            return;
        }

        await RunTickAsync(cancellationToken).ConfigureAwait(false);

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            await RunTickAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunTickAsync(CancellationToken cancellationToken)
    {
        try
        {
            await MonitorStateMachinesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Observe(
                LogLevel.Error,
                ex, values: [("Message", "State machine monitoring tick failed")]);
        }
    }

    public async Task<Outcome> StopMonitoringAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome.Failure();
        }

        if (_state.Stopping is null)
        {
            return Outcome.Success();
        }

        _state.Stopping.Cancel();
        _state.Timer?.Dispose();

        try
        {
            await _state.Loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _state.Stopping.Dispose();
            _state.Stopping = null;
            _state.Timer = null;
        }

        Observe(
            LogLevel.Information,
            null, values: [("Message", "State machine monitoring stopped")]);

        return Outcome.Success();
    }

    public void Dispose()
    {
        _state.Stopping?.Cancel();
        _state.Timer?.Dispose();
    }

    [Operation("MonitorStateMachines")]
    public async Task<Outcome> MonitorStateMachinesAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome.Failure();
        }

        var instances = _registryService.GetAllInstances().ToList();

        Observe(
            LogLevel.Debug,
            null, values: [("Message", "State machine registry size observed"), ("RegisteredInstanceCount", instances.Count)]);

        var errorCount = 0;
        var toEvict = new List<string>();
        var now = DateTime.UtcNow;

        foreach (var instance in instances)
        {
            try
            {
                if (ShouldEvict(instance, now))
                {
                    toEvict.Add(instance.InstanceId);
                    continue;
                }

                if (_monitor != null)
                {
                    var health = await _monitor.CheckHealthAsync(instance, cancellationToken).ConfigureAwait(false);
                    if (!health.IsSuccess)
                    {
                        errorCount++;

                        Observe(
                            LogLevel.Warning,
                            null, values: [("Reason", "State machine health check failed"), ("InstanceId", instance.InstanceId)]);
                    }
                }

                await CreateSnapshotAsync(instance.InstanceId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                errorCount++;

                Observe(
                    LogLevel.Error,
                    ex, values: [("Reason", "Error monitoring state machine"), ("InstanceId", instance.InstanceId)]);
            }
        }

        foreach (var instanceId in toEvict)
        {
            var unregisterResult = await _registryService.UnregisterStateMachineAsync(instanceId, cancellationToken).ConfigureAwait(false);
            if (!unregisterResult.IsSuccess)
            {
                errorCount++;

                Observe(
                    LogLevel.Warning,
                    null, values: [("Reason", "Eviction failed"), ("InstanceId", instanceId)]);
            }
            else
            {
                Observe(
                    LogLevel.Information,
                    null, values: [("Message", "Evicted state machine"), ("InstanceId", instanceId)]);
            }
        }

        if (errorCount > 0)
        {
            return Outcome.Failure();
        }

        return Outcome.Success();
    }

    private static bool ShouldEvict(IStateMachineInstance instance, DateTime now)
    {
        if (instance is not IStateMachineLifecycleInfo lifecycle)
        {
            return false;
        }

        if (lifecycle.IsTerminal)
        {
            return true;
        }

        var timeout = lifecycle.AutoCleanupTimeout;
        if (timeout is null
            || timeout.Value <= TimeSpan.Zero)
        {
            return false;
        }

        return now - lifecycle.LastActivity >= timeout.Value;
    }

    [Operation("CreateSnapshot")]
    public async Task<Outcome> CreateSnapshotAsync(
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

        var instance = _registryService.GetInstance(instanceId);
        if (instance == null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "State machine not found"), ("InstanceId", instanceId)]);
            return Outcome.Failure();
        }

        var snapshot = await instance.CreateSnapshot().ConfigureAwait(false);

        if (!snapshot.IsSuccess
            || snapshot.Data == null)
        {
            Observe(
                LogLevel.Error,
                null, values: [("Reason", "Snapshot creation failed"), ("InstanceId", instanceId)]);

            return Outcome.Failure();
        }

        if (snapshot.Data.Configuration?.Persist == false)
        {
            return Outcome.Success();
        }

        var saveResult = await _persistence.SaveSnapshotAsync(snapshot.Data, cancellationToken).ConfigureAwait(false);
        if (!saveResult.IsSuccess)
        {
            Observe(
                LogLevel.Error,
                null, values: [("Reason", "Failed to persist snapshot for state machine"), ("InstanceId", instanceId)]);

            return Outcome.Failure();
        }

        Observe(
            LogLevel.Debug,
            null, values: [("Message", "Created snapshot for state machine"), ("InstanceId", instanceId)]);

        return Outcome.Success();
    }
}
