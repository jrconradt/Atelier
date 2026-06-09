using Atelier.Framework.Context;
using Atelier.Framework.Observability.Formatting;
using Atelier.Framework.Observability.Strategy;
using Atelier.Framework.Testing;

namespace Atelier.Framework.Observability;

public static class LoggingStrategyBehaviorTests
{
    private sealed class RecordingStrategy : ILoggingStrategy
    {
        public List<LoggingContext> Received { get; } = new();

        public Task TraverseAsync(
            LoggingContext loggingContext,
            CancellationToken cancellationToken = default)
        {
            Received.Add(loggingContext);
            return Task.CompletedTask;
        }
    }

    private static LoggingContext MakeContext(
        string operationName,
        string message,
        LogLevel level,
        Exception? exception = null)
    {
        return new LoggingContext(
            global::Atelier.Framework.Context.Context.CreateSystemContext(operationName),
            message,
            exception,
            new Dictionary<string, object>(),
            level);
    }

    [GeneratedTest("Observability/Filter-Drops-Below-Predicate", "global::Atelier.Framework.Observability.FilterLoggingDecorator")]
    public static async Task FilterDropsEntriesThatFailPredicate()
    {
        var inner = new RecordingStrategy();
        var decorator = new FilterLoggingDecorator(
            inner,
            ctx => ctx.Level == LogLevel.Error);

        await decorator.TraverseAsync(MakeContext("op", "info line", LogLevel.Information)).ConfigureAwait(false);

        if (inner.Received.Count != 0)
        {
            throw new InvalidOperationException($"filter passed an entry that failed the predicate: {inner.Received.Count}");
        }
    }

    [GeneratedTest("Observability/Filter-Passes-Matching", "global::Atelier.Framework.Observability.FilterLoggingDecorator")]
    public static async Task FilterPassesEntriesThatMatchPredicate()
    {
        var inner = new RecordingStrategy();
        var decorator = new FilterLoggingDecorator(
            inner,
            ctx => ctx.Level == LogLevel.Error);

        await decorator.TraverseAsync(MakeContext("op", "boom", LogLevel.Error)).ConfigureAwait(false);

        if (inner.Received.Count != 1)
        {
            throw new InvalidOperationException($"filter dropped a matching entry: {inner.Received.Count}");
        }
    }

    [GeneratedTest("Observability/Filter-Only-Errors", "global::Atelier.Framework.Observability.FilterLoggingDecorator")]
    public static async Task OnlyErrorsExtensionForwardsOnlyExceptions()
    {
        var inner = new RecordingStrategy();
        var decorator = inner.OnlyErrors();

        await decorator.TraverseAsync(MakeContext("op", "fine", LogLevel.Information)).ConfigureAwait(false);
        await decorator.TraverseAsync(MakeContext("op", "bad", LogLevel.Error, new InvalidOperationException("x"))).ConfigureAwait(false);

        if (inner.Received.Count != 1)
        {
            throw new InvalidOperationException($"OnlyErrors forwarded the wrong count: {inner.Received.Count}");
        }
        if (inner.Received[0].Exception == null)
        {
            throw new InvalidOperationException("OnlyErrors forwarded an entry without an exception");
        }
    }

    [GeneratedTest("Observability/Throttle-Suppresses-Burst", "global::Atelier.Framework.Observability.ThrottleLoggingDecorator")]
    public static async Task ThrottleSuppressesRepeatsWithinWindow()
    {
        var inner = new RecordingStrategy();
        var decorator = new ThrottleLoggingDecorator(
            inner,
            TimeSpan.FromHours(1));

        for (var index = 0; index < 5; index++)
        {
            await decorator.TraverseAsync(MakeContext("op", "same line", LogLevel.Warning)).ConfigureAwait(false);
        }

        if (inner.Received.Count != 1)
        {
            throw new InvalidOperationException($"throttle let a burst through: {inner.Received.Count}");
        }
    }

    [GeneratedTest("Observability/Throttle-Allows-Distinct-Keys", "global::Atelier.Framework.Observability.ThrottleLoggingDecorator")]
    public static async Task ThrottleAllowsDistinctKeysThrough()
    {
        var inner = new RecordingStrategy();
        var decorator = new ThrottleLoggingDecorator(
            inner,
            TimeSpan.FromHours(1));

        await decorator.TraverseAsync(MakeContext("op", "line one", LogLevel.Warning)).ConfigureAwait(false);
        await decorator.TraverseAsync(MakeContext("op", "line two", LogLevel.Warning)).ConfigureAwait(false);

        if (inner.Received.Count != 2)
        {
            throw new InvalidOperationException($"throttle suppressed distinct messages: {inner.Received.Count}");
        }
    }

    [GeneratedTest("Observability/Composite-Fans-Out", "global::Atelier.Framework.Observability.Strategy.CompositeLoggingStrategy")]
    public static async Task CompositeFansOutToEveryChild()
    {
        var first = new RecordingStrategy();
        var second = new RecordingStrategy();
        var composite = new CompositeLoggingStrategy(first, second);

        await composite.TraverseAsync(MakeContext("op", "fan out", LogLevel.Information)).ConfigureAwait(false);

        if (first.Received.Count != 1
            || second.Received.Count != 1)
        {
            throw new InvalidOperationException($"composite did not fan out to all children: {first.Received.Count}/{second.Received.Count}");
        }
    }

    [GeneratedTest("Observability/Composite-Remove-Stops-Delivery", "global::Atelier.Framework.Observability.Strategy.CompositeLoggingStrategy")]
    public static async Task CompositeStopsDeliveringToRemovedChild()
    {
        var kept = new RecordingStrategy();
        var removed = new RecordingStrategy();
        var composite = new CompositeLoggingStrategy(kept, removed);
        composite.RemoveStrategy(removed);

        await composite.TraverseAsync(MakeContext("op", "after remove", LogLevel.Information)).ConfigureAwait(false);

        if (kept.Received.Count != 1)
        {
            throw new InvalidOperationException($"composite stopped delivering to a kept child: {kept.Received.Count}");
        }
        if (removed.Received.Count != 0)
        {
            throw new InvalidOperationException($"composite delivered to a removed child: {removed.Received.Count}");
        }
    }
}
