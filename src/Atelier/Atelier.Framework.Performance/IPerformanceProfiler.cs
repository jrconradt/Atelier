using Atelier.Framework.Context;
using Atelier.Framework.Outcomes;

namespace Atelier.Framework.Performance;

public interface IPerformanceProfiler
{
    public Task<Outcome<PerformanceSnapshot>> CaptureSnapshotAsync(
        CancellationToken cancellationToken = default);

    public Task<Outcome> RecordMetricAsync(
        PerformanceMetric metric,
        CancellationToken cancellationToken = default);

    public Task<Outcome<ComponentMetrics>> GetComponentMetricsAsync(
        string componentName,
        TimeSpan? window = null,
        CancellationToken cancellationToken = default);

    public Task<Outcome<List<PerformanceAlert>>> GetActiveAlertsAsync(
        AlertSeverity? minSeverity = null,
        CancellationToken cancellationToken = default);

    public Task<Outcome> RaiseAlertAsync(
        PerformanceAlert alert,
        CancellationToken cancellationToken = default);

    public Task<Outcome<PerformanceBaseline>> CreateBaselineAsync(
        string component,
        string operation,
        TimeSpan sampleWindow,
        CancellationToken cancellationToken = default);

    public Task<Outcome> DetectRegressionAsync(
        string component,
        string operation,
        double currentValue,
        CancellationToken cancellationToken = default);

    public IDisposable StartOperation(
        string component,
        string operation);
}

public interface IPerformanceMonitor
{
    public Task StartMonitoringAsync(
        CancellationToken cancellationToken = default);

    public Task StopMonitoringAsync();

    public Task<Outcome<Dictionary<string, ComponentMetrics>>> GetAllComponentMetricsAsync(
        TimeSpan? window = null,
        CancellationToken cancellationToken = default);

    public Task<Outcome<List<PerformanceMetric>>> QueryMetricsAsync(
        MetricQuery? query = null,
        CancellationToken cancellationToken = default);

    public Task<Outcome> RegisterBudgetAsync(
        PerformanceBudget budget,
        CancellationToken cancellationToken = default);
}
