using System.Collections.Concurrent;

namespace Atelier.Framework.Performance;

internal sealed class MetricStore
{
    private const int MAX_SAMPLES_PER_KEY = 4096;

    private readonly ConcurrentDictionary<string, ConcurrentQueue<PerformanceMetric>> _metrics = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _keysByComponent = new();
    private readonly TimeSpan _retentionWindow;

    public MetricStore(TimeSpan retentionWindow)
    {
        _retentionWindow = retentionWindow;
    }

    public void Record(PerformanceMetric metric)
    {
        var key = $"{metric.Component}:{metric.Operation}";
        var queue = _metrics.GetOrAdd(key, _ => new ConcurrentQueue<PerformanceMetric>());

        var componentKeys = _keysByComponent.GetOrAdd(metric.Component, _ => new ConcurrentDictionary<string, byte>());
        componentKeys.TryAdd(key, 0);

        queue.Enqueue(metric);
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
            if (!_metrics.TryGetValue(key, out var queue))
            {
                continue;
            }

            while (queue.TryPeek(out var oldest)
                   && oldest.Timestamp < windowStart)
            {
                queue.TryDequeue(out _);
            }

            while (queue.Count > MAX_SAMPLES_PER_KEY)
            {
                queue.TryDequeue(out _);
            }

            if (queue.IsEmpty)
            {
                if (_metrics.TryRemove(new KeyValuePair<string, ConcurrentQueue<PerformanceMetric>>(key, queue)))
                {
                    RemoveFromIndex(key);
                }
            }
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
