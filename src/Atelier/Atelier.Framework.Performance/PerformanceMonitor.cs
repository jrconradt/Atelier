using Atelier.Framework.Primitives;
using System.Collections.Concurrent;
using Atelier.Framework.Infrastructure;
using Atelier.Framework.Attributes;
using Atelier.Framework.Infrastructure.Operation;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;

namespace Atelier.Framework.Performance;

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class PerformanceMonitor : IAtelier, IPerformanceMonitor, Microsoft.Extensions.Hosting.IHostedService
{
    Task Microsoft.Extensions.Hosting.IHostedService.StartAsync(CancellationToken cancellationToken)
    {
        return StartMonitoringAsync(cancellationToken);
    }

    Task Microsoft.Extensions.Hosting.IHostedService.StopAsync(CancellationToken cancellationToken)
    {
        return StopMonitoringAsync();
    }

    private static readonly TimeSpan CollectionInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultMetricsWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MetricsRetention = TimeSpan.FromHours(1);

    [Requisite] protected readonly IPerformanceProfiler _profiler = null!;
    private readonly MetricStore _metricStore = new(MetricsRetention);
    private readonly ConcurrentDictionary<string, PerformanceBudget> _budgets = new();
    private readonly MonitoringState _state = new();

    private sealed class MonitoringState
    {
        public Timer? Timer;
        public int IsMonitoring;
        public int CollectionInFlight;
    }

    public Task StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _state.IsMonitoring, 1, 0) == 1)
        {
            Observe(LogLevel.Warning, values: [("Reason", "Monitoring already running"), ("IntervalSeconds", CollectionInterval.TotalSeconds)]);
            return Task.CompletedTask;
        }

        Observe(LogLevel.Information, values: [("State", "MonitoringStarted"), ("IntervalSeconds", CollectionInterval.TotalSeconds)]);

        _state.Timer = new Timer(
            _ => _ = CollectMetricsAsync(),
            null,
            TimeSpan.Zero,
            CollectionInterval);

        return Task.CompletedTask;
    }

    public Task StopMonitoringAsync()
    {
        if (Interlocked.CompareExchange(ref _state.IsMonitoring, 0, 1) == 0)
        {
            return Task.CompletedTask;
        }

        Observe(LogLevel.Information, values: [("State", "MonitoringStopped")]);

        _state.Timer?.Dispose();
        _state.Timer = null;

        return Task.CompletedTask;
    }

    [Operation("GetAllComponentMetrics")]
    public Task<Outcome<Dictionary<string, ComponentMetrics>>> GetAllComponentMetricsAsync(
        TimeSpan? window = null,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Outcome<Dictionary<string, ComponentMetrics>>.Failure());
        }

        try
        {
            var windowStart = DateTime.UtcNow - (window ?? DefaultMetricsWindow);
            var componentMetrics = new Dictionary<string, ComponentMetrics>();

            foreach (var (component, metrics) in _metricStore.SnapshotByComponent(windowStart))
            {
                if (metrics.Count == 0)
                {
                    continue;
                }

                componentMetrics[component] = MetricCalculations.CalculateComponentMetrics(
                    component,
                    metrics);
            }

            return Task.FromResult(Outcome<Dictionary<string, ComponentMetrics>>.Success(componentMetrics));
        }
        catch (Exception ex)
        {
            Observe(LogLevel.Error, ex, values: [("Reason", "Failed to get metrics")]);

            return Task.FromResult(Outcome<Dictionary<string, ComponentMetrics>>.Failure());
        }
    }

    [Operation("QueryMetrics")]
    public Task<Outcome<List<PerformanceMetric>>> QueryMetricsAsync(
        MetricQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Outcome<List<PerformanceMetric>>.Failure());
        }

        try
        {
            var snapshot = _metricStore.SnapshotAll(DateTime.UtcNow - MetricsRetention);

            var metrics = snapshot.AsEnumerable();

            if (query?.Component != null)
            {
                metrics = metrics.Where(m => m.Component == query.Component);
            }

            if (query?.Operation != null)
            {
                metrics = metrics.Where(m => m.Operation == query.Operation);
            }

            if (query?.Type.HasValue == true)
            {
                metrics = metrics.Where(m => m.Type == query.Type!.Value);
            }

            if (query?.FromTime.HasValue == true)
            {
                metrics = metrics.Where(m => m.Timestamp >= query.FromTime!.Value);
            }

            if (query?.ToTime.HasValue == true)
            {
                metrics = metrics.Where(m => m.Timestamp <= query.ToTime!.Value);
            }

            var results = metrics
                .OrderByDescending(m => m.Timestamp)
                .ToList();

            return Task.FromResult(Outcome<List<PerformanceMetric>>.Success(results));
        }
        catch (Exception ex)
        {
            Observe(LogLevel.Error, ex, values: [("Reason", "Failed to query metrics")]);

            return Task.FromResult(Outcome<List<PerformanceMetric>>.Failure());
        }
    }

    private async Task CollectMetricsAsync()
    {
        if (Interlocked.CompareExchange(ref _state.CollectionInFlight, 1, 0) == 1)
        {
            return;
        }

        try
        {
            var snapshotResult = await _profiler.CaptureSnapshotAsync().ConfigureAwait(false);

            if (snapshotResult.IsSuccess)
            {
                var snapshot = snapshotResult.Data!;

                foreach (var (componentName, metrics) in snapshot.Components)
                {
                    StoreComponentMetrics(componentName, metrics);
                    await EvaluateBudgetAsync(componentName, metrics).ConfigureAwait(false);
                }

                _metricStore.Prune();
            }
        }
        catch (Exception ex)
        {
            Observe(LogLevel.Error, ex);
        }
        finally
        {
            Interlocked.Exchange(ref _state.CollectionInFlight, 0);
        }
    }

    private void StoreComponentMetrics(string componentName, ComponentMetrics metrics)
    {
        var metric = new PerformanceMetric
        {
            MetricId = Guid.NewGuid().ToString("N"),
            Component = componentName,
            Operation = "aggregate",
            Type = MetricType.Latency,
            Value = metrics.AverageLatencyMs,
            Unit = "ms",
            Timestamp = DateTime.UtcNow,
            Tags = new Dictionary<string, object>
            {
                ["p50"] = metrics.P50LatencyMs,
                ["p95"] = metrics.P95LatencyMs,
                ["p99"] = metrics.P99LatencyMs,
                ["ops"] = metrics.TotalOperations,
                ["error_rate"] = metrics.ErrorRate
            }
        };

        _metricStore.Record(metric);
    }

    [Operation("RegisterBudget")]
    public Task<Outcome> RegisterBudgetAsync(
        PerformanceBudget budget,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Outcome.Failure());
        }

        if (budget is null)
        {
            Observe(LogLevel.Warning, values: [("Reason", $"{nameof(budget)} cannot be null")]);
            return Task.FromResult(Outcome.Failure());
        }

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "Component", budget.Component);

        _budgets[budget.Component] = budget;

        return Task.FromResult(Outcome.Success());
    }

    private async Task EvaluateBudgetAsync(
        string componentName,
        ComponentMetrics metrics)
    {
        if (!_budgets.TryGetValue(componentName, out var budget))
        {
            return;
        }

        var breaches = BudgetEvaluation.Evaluate(budget, metrics);

        foreach (var breach in breaches)
        {
            Observe(
                breach.Severity == AlertSeverity.Critical ? LogLevel.Error : LogLevel.Warning,
                values: [("Component", breach.Component), ("Metric", breach.Metric), ("Threshold", breach.Threshold), ("Actual", breach.ActualValue)]);

            var alert = new PerformanceAlert
            {
                AlertId = Guid.NewGuid().ToString("N"),
                Severity = breach.Severity,
                Component = breach.Component,
                Metric = breach.Metric,
                Threshold = breach.Threshold,
                ActualValue = breach.ActualValue,
                Message = $"Budget breach on {breach.Metric}: {breach.ActualValue:F2} exceeds {breach.Threshold:F2}"
            };

            await _profiler.RaiseAlertAsync(alert).ConfigureAwait(false);
        }
    }

}
