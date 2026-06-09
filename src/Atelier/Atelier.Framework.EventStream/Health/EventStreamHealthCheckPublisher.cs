using Atelier.Framework.Primitives;
using System.Collections.Concurrent;
using Atelier.Framework.Infrastructure;
using Atelier.Framework.Attributes;
using Atelier.Framework.Observability;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Atelier.Framework.EventStream.Health;

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class EventStreamHealthCheckPublisher : IAtelier, IHealthCheckPublisher
{
    private readonly ConcurrentDictionary<string, HealthStatus> _lastStatus = new();

    public Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
    {
        foreach (var entry in report.Entries)
        {
            var name = entry.Key;
            var current = entry.Value.Status;
            var previous = _lastStatus.TryGetValue(name, out var stored) ? stored : HealthStatus.Healthy;

            _lastStatus[name] = current;

            if (current == previous)
            {
                continue;
            }

            var level = current switch
            {
                HealthStatus.Unhealthy => LogLevel.Error,
                HealthStatus.Degraded => LogLevel.Warning,
                _ => LogLevel.Information
            };

            Observe(level, values: [("Check", name), ("PreviousStatus", previous.ToString()), ("CurrentStatus", current.ToString()), ("Description", entry.Value.Description ?? string.Empty)]);
        }

        return Task.CompletedTask;
    }
}
