using Atelier.Framework.Primitives;
using System.Collections.Concurrent;
using System.Diagnostics;
using Atelier.Framework.Context;
using Atelier.Framework.Infrastructure;
using Atelier.Framework.Attributes;
using Atelier.Framework.Infrastructure.Operation;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;

namespace Atelier.Framework.Performance;

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class PerformanceProfiler : IAtelier, IPerformanceProfiler, IAsyncDisposable
{
    private static readonly TimeSpan MetricRetentionWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DefaultMetricsWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan AlertRetentionWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan BaselineRetentionWindow = TimeSpan.FromHours(1);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(30);
    private const int MAX_ACTIVE_ALERTS = 1000;
    private const int MINIMUM_BASELINE_SAMPLES = 10;
    private const double DEFAULT_ACCEPTABLE_DEVIATION_PERCENT = 20.0;
    private const double CRITICAL_REGRESSION_PERCENT = 50.0;
    private const double HIGH_LATENCY_THRESHOLD_MS = 1000.0;
    private const double CRITICAL_LATENCY_THRESHOLD_MS = 5000.0;

    protected IContext? Context => AmbientContext.Current;

    private readonly MetricStore _metrics = new(MetricRetentionWindow);
    private readonly BaselineRegistry _baselines = new(BaselineRetentionWindow);
    private readonly AlertSink _alerts = new(AlertRetentionWindow, MAX_ACTIVE_ALERTS);
    private readonly ProcessResourceSampler _sampler = new();
    private readonly ConcurrentDictionary<Guid, Task> _outstanding = new();
    private readonly Timer _sweepTimer;
    private readonly ProfilerLifecycle _lifecycle = new();

    private sealed class ProfilerLifecycle
    {
        public int Disposed;
    }

    public PerformanceProfiler()
    {
        _sweepTimer = new Timer(
            _ => Sweep(),
            null,
            SweepInterval,
            SweepInterval);
    }

    [Operation("CaptureSnapshot")]
    public async Task<Outcome<PerformanceSnapshot>> CaptureSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome<PerformanceSnapshot>.Failure();
        }

        Observe(LogLevel.Information, values: [("Operation", "CaptureSnapshot")]);

        try
        {
            var systemMetrics = CaptureSystemMetrics();
            var windowStart = DateTime.UtcNow - MetricRetentionWindow;
            var byComponent = _metrics.SnapshotByComponent(windowStart);
            var componentMetrics = new Dictionary<string, ComponentMetrics>();

            foreach (var (component, metrics) in byComponent)
            {
                componentMetrics[component] = MetricCalculations.CalculateComponentMetrics(
                    component,
                    metrics);
            }

            var snapshot = new PerformanceSnapshot
            {
                SnapshotId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                System = systemMetrics,
                Components = componentMetrics,
                Alerts = _alerts.Snapshot()
            };

            return Outcome<PerformanceSnapshot>.Success(snapshot);
        }
        catch (Exception ex)
        {
            Observe(LogLevel.Error, ex, values: [("Reason", "Failed to capture snapshot")]);

            return Outcome<PerformanceSnapshot>.Failure();
        }
    }

    [Operation("RecordMetric")]
    public async Task<Outcome> RecordMetricAsync(
        PerformanceMetric metric,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome.Failure();
        }

        if (metric is null)
        {
            Observe(LogLevel.Warning, values: [("Reason", $"{nameof(metric)} cannot be null")]);
            return Outcome.Failure();
        }


        if (Volatile.Read(ref _lifecycle.Disposed) == 1)
        {
            Observe(LogLevel.Warning, values: [("Reason", "Profiler has been disposed"), ("Component", metric.Component)]);
            return Outcome.Failure();
        }

        try
        {
            _metrics.Record(metric);

            await CheckThresholdsAsync(metric, cancellationToken).ConfigureAwait(false);

            return Outcome.Success();
        }
        catch (Exception ex)
        {
            Observe(LogLevel.Error, ex, values: [("Reason", "Failed to record metric"), ("Component", metric.Component), ("Operation", metric.Operation)]);

            return Outcome.Failure();
        }
    }

    [Operation("GetComponentMetrics")]
    public Task<Outcome<ComponentMetrics>> GetComponentMetricsAsync(
        string componentName,
        TimeSpan? window = null,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Outcome<ComponentMetrics>.Failure());
        }

        if (componentName is null)
        {
            Observe(LogLevel.Warning, values: [("Reason", $"{nameof(componentName)} cannot be null")]);
            return Task.FromResult(Outcome<ComponentMetrics>.Failure());
        }


        try
        {
            var windowStart = DateTime.UtcNow - (window ?? DefaultMetricsWindow);
            var componentMetrics = _metrics.SnapshotByPrefix($"{componentName}:", windowStart);

            var metrics = MetricCalculations.CalculateComponentMetrics(componentName, componentMetrics);
            return Task.FromResult(Outcome<ComponentMetrics>.Success(metrics));
        }
        catch (Exception ex)
        {
            Observe(LogLevel.Error, ex, values: [("Reason", "Failed to get component metrics"), ("Component", componentName)]);

            return Task.FromResult(Outcome<ComponentMetrics>.Failure());
        }
    }

    [Operation("GetActiveAlerts")]
    public Task<Outcome<List<PerformanceAlert>>> GetActiveAlertsAsync(
        AlertSeverity? minSeverity = null,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Outcome<List<PerformanceAlert>>.Failure());
        }

        try
        {
            var alerts = _alerts.Snapshot(minSeverity);

            return Task.FromResult(Outcome<List<PerformanceAlert>>.Success(alerts));
        }
        catch (Exception ex)
        {
            Observe(LogLevel.Error, ex, values: [("Reason", "Failed to get alerts")]);

            return Task.FromResult(Outcome<List<PerformanceAlert>>.Failure());
        }
    }

    [Operation("RaiseAlert")]
    public Task<Outcome> RaiseAlertAsync(
        PerformanceAlert alert,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Outcome.Failure());
        }

        if (alert is null)
        {
            Observe(LogLevel.Warning, values: [("Reason", $"{nameof(alert)} cannot be null")]);
            return Task.FromResult(Outcome.Failure());
        }


        Observe(
            alert.Severity == AlertSeverity.Critical ? LogLevel.Error : LogLevel.Warning,
            values: [("AlertId", alert.AlertId), ("Severity", alert.Severity), ("Component", alert.Component), ("Metric", alert.Metric), ("Threshold", alert.Threshold), ("ActualValue", alert.ActualValue), ("Message", alert.Message), ("Timestamp", alert.Timestamp)]);

        _alerts.Add(alert);

        return Task.FromResult(Outcome.Success());
    }

    [Operation("CreateBaseline")]
    public Task<Outcome<PerformanceBaseline>> CreateBaselineAsync(
        string component,
        string operation,
        TimeSpan sampleWindow,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Outcome<PerformanceBaseline>.Failure());
        }

        if (component is null)
        {
            Observe(LogLevel.Warning, values: [("Reason", $"{nameof(component)} cannot be null")]);
            return Task.FromResult(Outcome<PerformanceBaseline>.Failure());
        }

        if (operation is null)
        {
            Observe(LogLevel.Warning, values: [("Reason", $"{nameof(operation)} cannot be null")]);
            return Task.FromResult(Outcome<PerformanceBaseline>.Failure());
        }


        Observe(LogLevel.Information, values: [("Component", component), ("Operation", operation)]);

        try
        {
            var windowStart = DateTime.UtcNow - sampleWindow;
            var key = $"{component}:{operation}";
            var samples = _metrics
                .SnapshotKey(key, windowStart)
                .Select(m => m.Value)
                .ToList();

            if (samples.Count == 0)
            {
                Observe(LogLevel.Warning, values: [("Reason", "No metrics found for baseline"), ("Component", component), ("Operation", operation)]);
                return Task.FromResult(Outcome<PerformanceBaseline>.Failure());
            }

            if (samples.Count < MINIMUM_BASELINE_SAMPLES)
            {
                Observe(LogLevel.Warning, values: [("Reason", "Insufficient samples for baseline"), ("Component", component), ("Operation", operation), ("Samples", samples.Count)]);
                return Task.FromResult(Outcome<PerformanceBaseline>.Failure());
            }

            var mean = samples.Average();
            var variance = samples.Sum(v => Math.Pow(v - mean, 2)) / samples.Count;
            var stdDev = Math.Sqrt(variance);

            var baseline = new PerformanceBaseline
            {
                BaselineId = Guid.NewGuid().ToString("N"),
                Component = component,
                Operation = operation,
                BaselineValue = mean,
                StandardDeviation = stdDev,
                AcceptableDeviationPercent = DEFAULT_ACCEPTABLE_DEVIATION_PERCENT,
                CreatedAt = DateTime.UtcNow,
                SampleCount = samples.Count
            };

            _baselines.Set(key, baseline);

            Observe(LogLevel.Information, values: [("Mean", mean), ("StdDev", stdDev), ("Samples", samples.Count)]);

            return Task.FromResult(Outcome<PerformanceBaseline>.Success(baseline));
        }
        catch (Exception ex)
        {
            Observe(LogLevel.Error, ex, values: [("Reason", "Failed to create baseline"), ("Component", component), ("Operation", operation)]);

            return Task.FromResult(Outcome<PerformanceBaseline>.Failure());
        }
    }

    [Operation("DetectRegression")]
    public Task<Outcome> DetectRegressionAsync(
        string component,
        string operation,
        double currentValue,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Outcome.Failure());
        }


        try
        {
            var key = $"{component}:{operation}";

            if (!_baselines.TryGet(key, out var baseline))
            {
                Observe(LogLevel.Warning, values: [("Reason", "No baseline found"), ("Component", component), ("Operation", operation)]);
                return Task.FromResult(Outcome.Failure());
            }

            var deviationPercent = Math.Abs(
                (currentValue - baseline.BaselineValue) / baseline.BaselineValue * 100);

            var isRegression = deviationPercent > baseline.AcceptableDeviationPercent;

            if (isRegression)
            {
                Observe(LogLevel.Warning, values: [("Component", component), ("Operation", operation), ("Current", currentValue), ("Baseline", baseline.BaselineValue), ("Deviation", deviationPercent)]);

                var alert = new PerformanceAlert
                {
                    AlertId = Guid.NewGuid().ToString("N"),
                    Severity = deviationPercent > CRITICAL_REGRESSION_PERCENT ? AlertSeverity.Critical : AlertSeverity.Warning,
                    Component = component,
                    Metric = operation,
                    Threshold = baseline.BaselineValue * (1 + baseline.AcceptableDeviationPercent / 100),
                    ActualValue = currentValue,
                    Message = $"Performance regression: {deviationPercent:F1}% slower than baseline"
                };

                _alerts.Add(alert);
            }

            return Task.FromResult(Outcome.Success());
        }
        catch (Exception ex)
        {
            Observe(LogLevel.Error, ex, values: [("Reason", "Failed to detect regression"), ("Component", component), ("Operation", operation)]);

            return Task.FromResult(Outcome.Failure());
        }
    }

    public IDisposable StartOperation(
        string component,
        string operation)
    {
        return new OperationTimer(this, component, operation);
    }

    private SystemMetrics CaptureSystemMetrics()
    {
        var sample = _sampler.Current;
        var totalAvailable = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;

        return new SystemMetrics
        {
            CpuUsagePercent = sample.CpuUsagePercent,
            MemoryUsedBytes = sample.WorkingSetBytes,
            MemoryAvailableBytes = totalAvailable,
            MemoryUsagePercent = totalAvailable > 0 ? (double)sample.WorkingSetBytes / totalAvailable * 100 : 0,
            ThreadCount = sample.ThreadCount,
            HandleCount = sample.HandleCount,
            DiskReadBytesPerSec = 0,
            DiskWriteBytesPerSec = 0,
            NetworkSentBytesPerSec = 0,
            NetworkReceivedBytesPerSec = 0,
            DiskMetricsAvailable = false,
            NetworkMetricsAvailable = false
        };
    }

    private Task CheckThresholdsAsync(
        PerformanceMetric metric,
        CancellationToken cancellationToken)
    {
        if (metric.Type == MetricType.Latency
            && metric.Value > HIGH_LATENCY_THRESHOLD_MS)
        {
            var alert = new PerformanceAlert
            {
                AlertId = Guid.NewGuid().ToString("N"),
                Severity = metric.Value > CRITICAL_LATENCY_THRESHOLD_MS ? AlertSeverity.Critical : AlertSeverity.Warning,
                Component = metric.Component,
                Metric = metric.Operation,
                Threshold = HIGH_LATENCY_THRESHOLD_MS,
                ActualValue = metric.Value,
                Message = $"High latency detected: {metric.Value:F2}ms"
            };

            _alerts.Add(alert);
        }

        return Task.CompletedTask;
    }

    private void Sweep()
    {
        _metrics.Prune();
        _baselines.Evict();
    }

    private void Track(Task task)
    {
        var id = Guid.NewGuid();
        _outstanding[id] = task;
        _ = task.ContinueWith(
            completed => _outstanding.TryRemove(id, out _),
            TaskScheduler.Default);
    }

    private class OperationTimer : IDisposable
    {
        private readonly PerformanceProfiler _profiler;
        private readonly string _component;
        private readonly string _operation;
        private readonly Stopwatch _stopwatch;

        public OperationTimer(
            PerformanceProfiler profiler,
            string component,
            string operation)
        {
            _profiler = profiler;
            _component = component;
            _operation = operation;
            _stopwatch = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            _stopwatch.Stop();

            var metric = new PerformanceMetric
            {
                MetricId = Guid.NewGuid().ToString("N"),
                Component = _component,
                Operation = _operation,
                Type = MetricType.Latency,
                Value = _stopwatch.Elapsed.TotalMilliseconds,
                Unit = "ms",
                Timestamp = DateTime.UtcNow
            };

            if (Volatile.Read(ref _profiler._lifecycle.Disposed) == 1)
            {
                return;
            }

            var task = RecordAndObserveAsync(metric);
            _profiler.Track(task);
        }

        private async Task RecordAndObserveAsync(PerformanceMetric metric)
        {
            try
            {
                var outcome = await _profiler.RecordMetricAsync(metric).ConfigureAwait(false);
                if (!outcome.IsSuccess)
                {
                    _profiler.Observe(
                        LogLevel.Warning,
                        values: [("Reason", "RecordMetric reported failure"), ("Component", _component), ("Operation", _operation)]);
                }
            }
            catch (Exception ex)
            {
                _profiler.Observe(LogLevel.Error, ex);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _lifecycle.Disposed, 1, 0) == 1)
        {
            return;
        }

        _sweepTimer.Dispose();

        var pending = _outstanding.Values.ToArray();
        await Task.WhenAll(pending).ConfigureAwait(false);

        await _sampler.DisposeAsync().ConfigureAwait(false);
    }
}
