using Atelier.Framework.Primitives;
using Atelier.Framework.Infrastructure;
using Atelier.Framework.Attributes;
using Atelier.Framework.Observability;
using Atelier.Framework.Infrastructure.Operation;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;

namespace Atelier.Framework.Queueing.Primitives;

[Infrastructure(InfrastructureLifetime.Scoped)]
public partial class TaskQueueHealthCheck<T> : IAtelier, Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    [Requisite] protected readonly ITaskQueue<T> _queue = null!;
    private TaskQueueHealthConfiguration _configuration = new();


    protected TaskQueueHealthCheck() { }

    public TaskQueueHealthCheck<T> Configure(TaskQueueHealthConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        return this;
    }

    async Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck.CheckHealthAsync(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context,
        CancellationToken cancellationToken)
    {
        var outcome = await CheckHealthAsync(cancellationToken).ConfigureAwait(false);
        if (!outcome.IsSuccess)
        {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy(
                "Task queue health check failed.");
        }

        var status = outcome.Data!;
        var data = new Dictionary<string, object>
        {
            ["Message"] = status.Message
        };

        if (status.Metrics is not null)
        {
            data["CurrentCount"] = status.Metrics.CurrentCount;
            data["Capacity"] = status.Metrics.Capacity;
            data["Utilization"] = status.Metrics.UtilizationPercent;
        }

        if (status.Status == HealthStatus.Unhealthy)
        {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy(
                status.Message,
                status.Exception,
                data);
        }

        if (status.Status == HealthStatus.Degraded)
        {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Degraded(
                status.Message,
                data: data);
        }

        return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(
            status.Message,
            data);
    }

    [Operation("CheckHealth")]
    public Task<Outcome<TaskQueueHealthStatus>> CheckHealthAsync(
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Outcome<TaskQueueHealthStatus>.Failure());
        }

        try
        {
            var metrics = _queue.GetMetrics();
            var status = DetermineHealthStatus(metrics);

            Observe(status.Status == HealthStatus.Healthy ? LogLevel.Debug : LogLevel.Warning, values: [("Status", status.Status.ToString()), ("QueueCount", metrics.CurrentCount), ("Capacity", metrics.Capacity), ("Utilization", metrics.UtilizationPercent)]);

            return Task.FromResult(Outcome<TaskQueueHealthStatus>.Success(status));
        }
        catch (Exception ex)
        {
            Observe(LogLevel.Error, ex);

            var errorStatus = new TaskQueueHealthStatus
            {
                Status = HealthStatus.Unhealthy,
                Message = "Health check failed due to exception",
                Exception = ex
            };

            return Task.FromResult(Outcome<TaskQueueHealthStatus>.Success(errorStatus));
        }
    }

    private TaskQueueHealthStatus DetermineHealthStatus(TaskQueueMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        if (_queue.IsCompleted && metrics.CurrentCount == 0)
        {
            return new TaskQueueHealthStatus
            {
                Status = HealthStatus.Healthy,
                Message = "Queue is completed and drained",
                Metrics = metrics
            };
        }

        if (metrics.UtilizationPercent >= _configuration.CriticalThresholdPercent)
        {
            return new TaskQueueHealthStatus
            {
                Status = HealthStatus.Unhealthy,
                Message = $"Queue utilization critical: {metrics.UtilizationPercent:F1}%",
                Metrics = metrics
            };
        }

        if (metrics.UtilizationPercent >= _configuration.WarningThresholdPercent)
        {
            return new TaskQueueHealthStatus
            {
                Status = HealthStatus.Degraded,
                Message = $"Queue utilization high: {metrics.UtilizationPercent:F1}%",
                Metrics = metrics
            };
        }

        if (metrics.TotalRejected > 0)
        {
            var rejectionRate = metrics.TotalEnqueued > 0
                ? (metrics.TotalRejected * 100.0 / metrics.TotalEnqueued)
                : 0;

            if (rejectionRate >= _configuration.MaxRejectionRatePercent)
            {
                return new TaskQueueHealthStatus
                {
                    Status = HealthStatus.Degraded,
                    Message = $"High rejection rate: {rejectionRate:F1}%",
                    Metrics = metrics
                };
            }
        }

        return new TaskQueueHealthStatus
        {
            Status = HealthStatus.Healthy,
            Message = $"Queue operating normally: {metrics.UtilizationPercent:F1}% utilized",
            Metrics = metrics
        };
    }
}

public enum HealthStatus
{
    Healthy,
    Degraded,
    Unhealthy
}

[ContractAttribute(
    "TaskQueueHealthStatus",
    Version = "1.0",
    Namespace = "Framework.Queueing.Primitives")]
public class TaskQueueHealthStatus
{
    public HealthStatus Status { get; init; }
    public string Message { get; init; } = string.Empty;
    public TaskQueueMetrics? Metrics { get; init; }
    public Exception? Exception { get; init; }
}

[Infrastructure(InfrastructureLifetime.Singleton)]
public class TaskQueueHealthConfiguration
{
    public double WarningThresholdPercent { get; set; } = 75.0;
    public double CriticalThresholdPercent { get; set; } = 90.0;
    public double MaxRejectionRatePercent { get; set; } = 10.0;
}
