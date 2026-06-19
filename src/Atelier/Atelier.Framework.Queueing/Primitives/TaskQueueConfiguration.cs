namespace Atelier.Framework.Queueing.Primitives;

public class TaskQueueConfiguration
{
    public int Capacity { get; set; } = 1000;
    public TimeSpan DefaultEnqueueTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan DefaultDequeueTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public bool EnableMetrics { get; set; } = true;
    public bool EnableDetailedLogging { get; set; } = false;
}
