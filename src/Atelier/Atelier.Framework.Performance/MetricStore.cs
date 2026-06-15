using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace Atelier.Framework.Performance;

internal sealed class MetricStore
{
    private const int MAX_SAMPLES_PER_KEY = 4096;

    private readonly ConcurrentDictionary<string, ImmutableQueue<PerformanceMetric>> _metrics = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _keysByComponent = new();
    private readonly TimeSpan _retentionWindow;

    public MetricStore(TimeSpan retentionWindow)
    {
        _retentionWindow = retentionWindow;
    }

    public void Record(PerformanceMetric metric)
    {
        var key = $"{metric.Component}:{metric.Operation}";

        var componentKeys = _keysByComponent.GetOrAdd(metric.Component, _ => new ConcurrentDictionary<string, byte>());
        componentKeys.TryAdd(key, 0);

        _metrics.AddOrUpdate(key,
                             _ => ImmutableQueue.Create(metric),
                             (_, existing) => existing.Enqueue(metric));
    }

    private DateTime EffectiveStart(DateTime windowStart)
    {
        var retentionStart = DateTime.UtcNow - _retentionWindow;
        return windowStart > retentionStart ? windowStart : retentionStart;
    }

    public List<PerformanceMetric> SnapshotByPrefix(
        string prefix,
        DateTime windowStart)
    {
        var results = new List<PerformanceMetric>();
        var component = ComponentFromPrefix(prefix);
        var effectiveStart = EffectiveStart(windowStart);

        if (!_keysByComponent.TryGetValue(component, out var componentKeys))
        {
            return results;
        }

        foreach (var key in componentKeys.Keys)
        {
            if (!key.StartsWith(prefix))
            {
                continue;
            }

            if (!_metrics.TryGetValue(key, out var queue))
            {
                continue;
            }

            foreach (var metric in queue)
            {
                if (metric.Timestamp >= effectiveStart)
                {
                    results.Add(metric);
                }
            }
        }

        return results;
    }

    public Dictionary<string, List<PerformanceMetric>> SnapshotByComponent(DateTime windowStart)
    {
        var byComponent = new Dictionary<string, List<PerformanceMetric>>();
        var effectiveStart = EffectiveStart(windowStart);

        foreach (var componentEntry in _keysByComponent)
        {
            var list = new List<PerformanceMetric>();

            foreach (var key in componentEntry.Value.Keys)
            {
                if (!_metrics.TryGetValue(key, out var queue))
                {
                    continue;
                }

                foreach (var metric in queue)
                {
                    if (metric.Timestamp >= effectiveStart)
                    {
                        list.Add(metric);
                    }
                }
            }

            byComponent[componentEntry.Key] = list;
        }

        return byComponent;
    }

    public List<PerformanceMetric> SnapshotKey(
        string key,
        DateTime windowStart)
    {
        var results = new List<PerformanceMetric>();
        var effectiveStart = EffectiveStart(windowStart);

        if (!_metrics.TryGetValue(key, out var queue))
        {
            return results;
        }

        foreach (var metric in queue)
        {
            if (metric.Timestamp >= effectiveStart)
            {
                results.Add(metric);
            }
        }

        return results;
    }

    public void Prune()
    {
        var windowStart = DateTime.UtcNow - _retentionWindow;

        foreach (var key in _metrics.Keys)
        {
            if (!_metrics.TryGetValue(key, out var snapshot))
            {
                continue;
            }

            var retained = new List<PerformanceMetric>();
            var original = 0;

            foreach (var metric in snapshot)
            {
                original++;

                if (metric.Timestamp >= windowStart)
                {
                    retained.Add(metric);
                }
            }

            var excess = retained.Count - MAX_SAMPLES_PER_KEY;

            if (excess > 0)
            {
                retained.RemoveRange(0, excess);
            }

            if (retained.Count == 0)
            {
                if (_metrics.TryRemove(new KeyValuePair<string, ImmutableQueue<PerformanceMetric>>(key, snapshot)))
                {
                    RemoveFromIndex(key);
                }

                continue;
            }

            if (retained.Count == original)
            {
                continue;
            }

            _metrics.TryUpdate(key,
                               ImmutableQueue.CreateRange(retained),
                               snapshot);
        }
    }

    private void RemoveFromIndex(string key)
    {
        var component = ComponentFromPrefix(key);

        if (!_keysByComponent.TryGetValue(component, out var componentKeys))
        {
            return;
        }

        componentKeys.TryRemove(key, out _);

        if (componentKeys.IsEmpty)
        {
            _keysByComponent.TryRemove(new KeyValuePair<string, ConcurrentDictionary<string, byte>>(component, componentKeys));
        }
    }

    private static string ComponentFromPrefix(string value)
    {
        var separatorIndex = value.IndexOf(':');

        if (separatorIndex < 0)
        {
            return value;
        }

        return value.Substring(0, separatorIndex);
    }
}
