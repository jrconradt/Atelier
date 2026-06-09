using Atelier.Framework.Primitives;
using Atelier.Framework.Infrastructure;
using Atelier.Framework.Observability;
using Atelier.Framework.Requisitions;
using Atelier.Framework.StateMachine.Service;
using Microsoft.Extensions.Hosting;
using Atelier.Framework.Attributes;

namespace Atelier.Framework.StateMachine.Services;

[Infrastructure(InfrastructureLifetime.Singleton)]

public partial class StateMachineRetentionService : IAtelier, IHostedService
{
    [Requisite] protected readonly IStateMachinePersistence _persistence = null!;

    private readonly TimeSpan _sweepInterval = TimeSpan.FromMinutes(15);
    private readonly TimeSpan _retentionAge = TimeSpan.FromDays(7);
    private readonly RetentionState _state = new();

    private sealed class RetentionState
    {
        public PeriodicTimer? Timer;
        public Task Loop = Task.CompletedTask;
        public CancellationTokenSource? Stopping;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _state.Stopping = new CancellationTokenSource();
        _state.Timer = new PeriodicTimer(_sweepInterval);
        _state.Loop = RunSweepLoopAsync(_state.Stopping.Token);

        Observe(
            LogLevel.Information,
            null, values: [("Message", "State machine snapshot retention started"), ("SweepIntervalMinutes", _sweepInterval.TotalMinutes), ("RetentionDays", _retentionAge.TotalDays)]);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _state.Stopping?.Cancel();
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
            _state.Stopping?.Dispose();
            _state.Stopping = null;
        }

        Observe(
            LogLevel.Information,
            null, values: [("Message", "State machine snapshot retention stopped")]);
    }

    private async Task RunSweepLoopAsync(CancellationToken cancellationToken)
    {
        var timer = _state.Timer;
        if (timer is null)
        {
            return;
        }

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            await SweepAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _persistence.CleanupSnapshotsAsync(_retentionAge, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                Observe(
                    LogLevel.Error,
                    null, values: [("Message", "State machine snapshot retention sweep failed")]);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Observe(
                LogLevel.Error,
                ex, values: [("Message", "State machine snapshot retention sweep threw an unhandled exception")]);
        }
    }
}
