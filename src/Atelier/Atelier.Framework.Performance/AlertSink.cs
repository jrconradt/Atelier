using System.Collections.Concurrent;

namespace Atelier.Framework.Performance;

internal sealed class AlertSink
{
    private readonly ConcurrentQueue<PerformanceAlert> _alerts = new();
    private readonly TimeSpan _retentionWindow;
    private readonly int _maxActiveAlerts;
    private readonly TimeProvider _timeProvider;

    public AlertSink(
        TimeSpan retentionWindow,
        int maxActiveAlerts)
        : this(retentionWindow, maxActiveAlerts, TimeProvider.System)
    {
    }

    public AlertSink(
        TimeSpan retentionWindow,
        int maxActiveAlerts,
        TimeProvider timeProvider)
    {
        _retentionWindow = retentionWindow;
        _maxActiveAlerts = maxActiveAlerts;
        _timeProvider = timeProvider;
    }

    public void Add(PerformanceAlert alert)
    {
        _alerts.Enqueue(alert);

        var cutoff = _timeProvider.GetUtcNow().UtcDateTime - _retentionWindow;

        while (_alerts.TryPeek(out var oldest)
               && oldest.Timestamp < cutoff)
        {
            _alerts.TryDequeue(out _);
        }

        while (_alerts.Count > _maxActiveAlerts)
        {
            _alerts.TryDequeue(out _);
        }
    }

    public List<PerformanceAlert> Snapshot()
    {
        return _alerts.ToList();
    }

    public List<PerformanceAlert> Snapshot(AlertSeverity? minSeverity)
    {
        return _alerts
            .Where(a => !minSeverity.HasValue || a.Severity >= minSeverity.Value)
            .OrderByDescending(a => a.Severity)
            .ThenByDescending(a => a.Timestamp)
            .ToList();
    }
}
