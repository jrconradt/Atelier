using Atelier.Framework.Outcomes;

namespace Atelier.Framework.Queueing.Primitives;

public interface ITaskQueue<T> : IDisposable
{
    int Count { get; }
    int Capacity { get; }
    bool IsCompleted { get; }
    bool IsEmpty { get; }

    Outcome TryEnqueue(
        T item,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    Outcome<T> TryDequeue(
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    Outcome MarkCompleted();

    IAsyncEnumerable<T> GetConsumingEnumerableAsync(
        CancellationToken cancellationToken = default);

    TaskQueueMetrics GetMetrics();
}
