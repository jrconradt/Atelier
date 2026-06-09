using System.Collections.Concurrent;

namespace Atelier.Framework.Performance;

internal sealed class BaselineRegistry
{
    private const int MAX_BASELINES = 1000;

    private readonly ConcurrentDictionary<string, PerformanceBaseline> _baselines = new();
    private readonly TimeSpan _retentionWindow;
    private readonly TimeProvider _timeProvider;

    public BaselineRegistry(TimeSpan retentionWindow)
        : this(retentionWindow, TimeProvider.System)
    {
    }

    public BaselineRegistry(
        TimeSpan retentionWindow,
        TimeProvider timeProvider)
    {
        _retentionWindow = retentionWindow;
        _timeProvider = timeProvider;
    }

    public void Set(
        string key,
        PerformanceBaseline baseline)
    {
        _baselines[key] = baseline;
    }

    public bool TryGet(
        string key,
        out PerformanceBaseline baseline)
    {
        return _baselines.TryGetValue(key, out baseline!);
    }

    public void Evict()
    {
        var cutoff = _timeProvider.GetUtcNow().UtcDateTime - _retentionWindow;

        foreach (var kvp in _baselines)
        {
            if (kvp.Value.CreatedAt < cutoff)
            {
                _baselines.TryRemove(new KeyValuePair<string, PerformanceBaseline>(kvp.Key, kvp.Value));
            }
        }

        var overflow = _baselines.Count - MAX_BASELINES;

        if (overflow > 0)
        {
            var oldest = _baselines
                .OrderBy(kvp => kvp.Value.CreatedAt)
                .Take(overflow)
                .ToList();

            foreach (var kvp in oldest)
            {
                _baselines.TryRemove(new KeyValuePair<string, PerformanceBaseline>(kvp.Key, kvp.Value));
            }
        }
    }
}
