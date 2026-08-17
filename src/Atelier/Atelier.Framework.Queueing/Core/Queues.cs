using Atelier.Framework.Primitives;
using Atelier.Framework.Context;
using Atelier.Framework.Infrastructure;
using Atelier.Framework.Attributes;
using Atelier.Framework.Observability;
using Atelier.Framework.Requisitions;

namespace Atelier.Framework.Queueing.Core;

[Infrastructure(InfrastructureLifetime.Singleton)]
public partial class Queues : IAtelier
{
    [Requisite] protected readonly IQueueRegistry _registry = null!;

    public QueueProxy BackgroundTasks => new(_registry, "system.background");

    public QueueProxy TelemetryEvents => new(_registry, "system.telemetry");

    public QueueProxy HealthEvents => new(_registry, "system.health");

    public QueueProxy Topic(string topic)
    {
        ArgumentNullException.ThrowIfNull(topic);
        return new(_registry, topic);
    }

    public static IEnumerable<string> GetWellKnownTopics()
    {
        return new[]
        {
            "system.background",
            "system.telemetry",
            "system.health"
        };
    }
}
