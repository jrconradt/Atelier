
namespace Atelier.Framework.Queueing.Primitives;

public record TaskQueueMetrics
{
    public long TotalEnqueued { get; init; }
    public long TotalDequeued { get; init; }
    public long TotalRejected { get; init; }
    public int CurrentCount { get; init; }
    public int Capacity { get; init; }
    public bool IsCompleted { get; init; }
    public double UtilizationPercent { get; init; }
    public DateTimeOffset LastEnqueuedAt { get; init; }
    public DateTimeOffset LastDequeuedAt { get; init; }
}
