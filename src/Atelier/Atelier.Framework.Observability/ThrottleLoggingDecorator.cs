using System.Collections.Concurrent;
using Atelier.Framework.Observability.Strategy;

namespace Atelier.Framework.Observability
{
    public sealed class ThrottleLoggingDecorator : LoggingStrategyDecorator
    {
        private const int MaxTrackedKeys = 4096;
        private static readonly TimeSpan EvictionInterval = TimeSpan.FromSeconds(30);
        private readonly TimeSpan _throttleWindow;
        private readonly ConcurrentDictionary<string, DateTime> _lastLogTimes = new();
        private long _nextEvictionTicks;

        public ThrottleLoggingDecorator(
            ILoggingStrategy innerStrategy,
            TimeSpan throttleWindow)
            : base(innerStrategy)
        {
            _throttleWindow = throttleWindow;
        }

        public override Task TraverseAsync(
            LoggingContext loggingContext,
            CancellationToken cancellationToken = default)
        {
            var key = GetThrottleKey(loggingContext);
            var now = DateTime.UtcNow;

            if (_lastLogTimes.TryGetValue(key, out var lastLogTime)
                && now - lastLogTime < _throttleWindow)
            {
                return Task.CompletedTask;
            }

            _lastLogTimes[key] = now;
            MaybeEvictStaleKeys(now);

            return _innerStrategy.TraverseAsync(loggingContext, cancellationToken);
        }

        private void MaybeEvictStaleKeys(DateTime now)
        {
            var nowTicks = now.Ticks;
            var dueTicks = Interlocked.Read(ref _nextEvictionTicks);
            if (nowTicks < dueTicks)
            {
                return;
            }

            var scheduled = nowTicks + EvictionInterval.Ticks;
            if (Interlocked.CompareExchange(ref _nextEvictionTicks, scheduled, dueTicks) != dueTicks)
            {
                return;
            }

            EvictStaleKeys(now);
        }

        private void EvictStaleKeys(DateTime now)
        {
            var expiry = _throttleWindow > TimeSpan.Zero ? _throttleWindow : TimeSpan.FromMinutes(1);
            foreach (var entry in _lastLogTimes)
            {
                if (now - entry.Value >= expiry)
                {
                    _lastLogTimes.TryRemove(entry.Key, out _);
                }
            }

            var overflow = _lastLogTimes.Count - MaxTrackedKeys;
            if (overflow > 0)
            {
                var oldest = _lastLogTimes
                    .OrderBy(e => e.Value)
                    .Take(overflow)
                    .Select(e => e.Key)
                    .ToList();

                foreach (var staleKey in oldest)
                {
                    _lastLogTimes.TryRemove(staleKey, out _);
                }
            }
        }

        private string GetThrottleKey(LoggingContext context)
        {
            var messageHash = (uint)StringComparer.Ordinal.GetHashCode(context.Message ?? string.Empty);
            return $"{context.Context.Name}:{context.Level}:{messageHash}";
        }
    }
}
