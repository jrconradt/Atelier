using Atelier.Framework.EventStream.Consumers;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Atelier.Framework.EventStream.Health;

public sealed class EventStreamHealthCheck : IHealthCheck
{
    private static readonly TimeSpan RecentFailureWindow = TimeSpan.FromMinutes(5);

    private readonly IEnumerable<TopicConsumptionProcessor> _consumers;

    public EventStreamHealthCheck(IEnumerable<TopicConsumptionProcessor> consumers)
    {
        _consumers = consumers;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var consumers = _consumers.ToArray();

        if (consumers.Length == 0)
        {
            return Task.FromResult(HealthCheckResult.Healthy("No event stream consumers registered."));
        }

        var stopped = new List<string>();
        var faulted = new List<string>();
        var data = new Dictionary<string, object>();
        var now = DateTime.UtcNow;

        foreach (var consumer in consumers)
        {
            var stats = consumer.GetStats();
            var failedRecently = stats.LastFailureUtc.HasValue
                && now - stats.LastFailureUtc.Value <= RecentFailureWindow;

            data[consumer.ConsumerName] = new Dictionary<string, object>
            {
                ["IsRunning"] = consumer.IsRunning,
                ["EventsProcessed"] = stats.EventsProcessed,
                ["EventsFailed"] = stats.EventsFailed,
                ["LastFailureUtc"] = stats.LastFailureUtc?.ToString("O") ?? string.Empty,
                ["Lag"] = stats.Lag
            };

            if (!consumer.IsRunning)
            {
                stopped.Add(consumer.ConsumerName);
            }

            if (failedRecently)
            {
                faulted.Add(consumer.ConsumerName);
            }
        }

        if (stopped.Count > 0)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Event stream consumers not running: {string.Join(", ", stopped)}.",
                data: data));
        }

        if (faulted.Count > 0)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Event stream consumers reporting failed events in the last {RecentFailureWindow.TotalMinutes:0} minutes: {string.Join(", ", faulted)}.",
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            $"All {consumers.Length} event stream consumers are running.",
            data));
    }
}
