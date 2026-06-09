using System.Collections.Concurrent;

namespace Atelier.Framework.StateMachine;

internal sealed class StateTransitionCoordinator
{
    private readonly ConcurrentQueue<DateTime> _recentTransitions = new();
    private int _inTransition;

    public Task<bool> EnterTransitionAsync(
        int? maxTransitionsPerMinute,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<bool>(cancellationToken);
        }

        if (Interlocked.CompareExchange(ref _inTransition, 1, 0) != 0)
        {
            return Task.FromResult(false);
        }

        if (!TryAdmit(maxTransitionsPerMinute))
        {
            Interlocked.Exchange(ref _inTransition, 0);
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    public void ExitTransition()
    {
        Interlocked.Exchange(ref _inTransition, 0);
    }

    public void RunStateChange(Action apply)
    {
        if (apply is null)
        {
            throw new ArgumentNullException(nameof(apply));
        }

        if (Interlocked.CompareExchange(ref _inTransition, 1, 0) != 0)
        {
            apply();
            return;
        }

        try
        {
            apply();
        }
        finally
        {
            Interlocked.Exchange(ref _inTransition, 0);
        }
    }

    public bool RunGuarded(Action apply)
    {
        if (apply is null)
        {
            throw new ArgumentNullException(nameof(apply));
        }

        if (Interlocked.CompareExchange(ref _inTransition, 1, 0) != 0)
        {
            return false;
        }

        try
        {
            apply();
        }
        finally
        {
            Interlocked.Exchange(ref _inTransition, 0);
        }

        return true;
    }

    private bool TryAdmit(int? maxTransitionsPerMinute)
    {
        if (maxTransitionsPerMinute is null
            || maxTransitionsPerMinute.Value <= 0)
        {
            return true;
        }

        var now = DateTime.UtcNow;
        var windowStart = now - TimeSpan.FromMinutes(1);

        while (_recentTransitions.TryPeek(out var oldest)
               && oldest < windowStart)
        {
            _recentTransitions.TryDequeue(out _);
        }

        if (_recentTransitions.Count >= maxTransitionsPerMinute.Value)
        {
            return false;
        }

        _recentTransitions.Enqueue(now);
        return true;
    }
}
