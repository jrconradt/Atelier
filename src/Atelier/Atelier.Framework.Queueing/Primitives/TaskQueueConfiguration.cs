using Atelier.Framework.Primitives;
using Atelier.Framework.Attributes;

namespace Atelier.Framework.Queueing.Primitives;

[Infrastructure(InfrastructureLifetime.Singleton)]
public class TaskQueueConfiguration
{
    public int Capacity { get; set; } = 1000;
    public TimeSpan DefaultEnqueueTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan DefaultDequeueTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public bool EnableMetrics { get; set; } = true;
    public bool EnableDetailedLogging { get; set; } = false;
}
