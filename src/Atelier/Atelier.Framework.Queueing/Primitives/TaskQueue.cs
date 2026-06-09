using Atelier.Framework.Primitives;
using Atelier.Framework.Infrastructure;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Atelier.Framework.Attributes;
using Atelier.Framework.Observability;
using Atelier.Framework.Infrastructure.Operation;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;

namespace Atelier.Framework.Queueing.Primitives;

[Infrastructure(InfrastructureLifetime.Scoped)]
public partial class TaskQueue<T> : IAtelier, ITaskQueue<T>
{
    private TaskQueueConfiguration _configuration = new();
    private BlockingCollection<T> _queue;
    private readonly InternalMetrics _metrics = new();
    private bool _disposed;



    public TaskQueue()
    {
        _queue = new BlockingCollection<T>(boundedCapacity: _configuration.Capacity);
    }

    public TaskQueue<T> Configure(TaskQueueConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

        var old = _queue;
        _queue = new BlockingCollection<T>(boundedCapacity: _configuration.Capacity);
        old?.Dispose();
        return this;
    }

    public int Count => _queue.Count;
    public int Capacity => _configuration.Capacity;
    public bool IsCompleted => _queue.IsAddingCompleted;
    public bool IsEmpty => _queue.Count == 0;

    [Operation("TryEnqueue")]
    public Outcome TryEnqueue(
        T item,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome.Failure();
        }

        if (_disposed)
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", "Queue has been disposed")]);
            return Outcome.Failure();
        }

        try
        {
            var added = _queue.TryAdd(item, (int)timeout.TotalMilliseconds, cancellationToken);

            if (!added)
            {
                _metrics.RecordRejection();

                Observe(
                    LogLevel.Warning,
                    values: [("Reason", "Queue is full or timeout exceeded"), ("QueueCount", Count), ("Capacity", Capacity), ("TimeoutMs", timeout.TotalMilliseconds)]);

                return Outcome.Failure();
            }

            _metrics.RecordEnqueue();

            if (_configuration.EnableDetailedLogging)
            {
                Observe(LogLevel.Debug, values: [("QueueCount", Count), ("Capacity", Capacity)]);
            }

            return Outcome.Success();
        }
        catch (OperationCanceledException)
        {
            Observe(
                LogLevel.Information,
                values: [("Reason", "Enqueue operation was cancelled")]);
            return Outcome.Failure();
        }
        catch (InvalidOperationException)
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", "Queue is completed and cannot accept new items"), ("QueueCount", Count)]);

            return Outcome.Failure();
        }
    }

    [Operation("TryDequeue")]
    public Outcome<T> TryDequeue(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome<T>.Failure();
        }

        if (_disposed)
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", "Queue has been disposed")]);
            return Outcome<T>.Failure();
        }

        try
        {
            var taken = _queue.TryTake(out var item, (int)timeout.TotalMilliseconds, cancellationToken);

            if (!taken)
            {
                if (_queue.IsCompleted)
                {
                    Observe(
                        LogLevel.Debug,
                        values: [("Reason", "Queue is completed and empty")]);

                    return Outcome<T>.Failure();
                }

                Observe(
                    LogLevel.Debug,
                    values: [("Reason", "No item available within timeout"), ("QueueCount", Count), ("TimeoutMs", timeout.TotalMilliseconds)]);

                return Outcome<T>.Failure();
            }

            _metrics.RecordDequeue();

            if (_configuration.EnableDetailedLogging)
            {
                Observe(LogLevel.Debug, values: [("QueueCount", Count)]);
            }

            return item!;
        }
        catch (OperationCanceledException)
        {
            Observe(
                LogLevel.Information,
                values: [("Reason", "Dequeue operation was cancelled")]);
            return Outcome<T>.Failure();
        }
    }

    [Operation("MarkCompleted")]
    public Outcome MarkCompleted()
    {
        if (_disposed)
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", "Queue has been disposed")]);
            return Outcome.Failure();
        }

        if (_queue.IsAddingCompleted)
        {
            Observe(
                LogLevel.Information,
                values: [("Reason", "Queue already marked as completed")]);
            return Outcome.Success();
        }

        _queue.CompleteAdding();

        Observe(LogLevel.Information, values: [("RemainingItems", Count)]);

        return Outcome.Success();
    }

    public async IAsyncEnumerable<T> GetConsumingEnumerableAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            yield break;
        }

        foreach (var item in _queue.GetConsumingEnumerable(cancellationToken))
        {
            _metrics.RecordDequeue();

            if (_configuration.EnableDetailedLogging)
            {
                Observe(LogLevel.Trace, values: [("QueueCount", Count)]);
            }

            yield return item;

            await Task.Yield();
        }
    }

    public TaskQueueMetrics GetMetrics()
    {
        var (enqueued, dequeued, rejected, lastEnqueued, lastDequeued) = _metrics.GetSnapshot();

        return new TaskQueueMetrics
        {
            TotalEnqueued = enqueued,
            TotalDequeued = dequeued,
            TotalRejected = rejected,
            CurrentCount = Count,
            Capacity = Capacity,
            IsCompleted = IsCompleted,
            UtilizationPercent = Capacity > 0 ? (Count * 100.0 / Capacity) : 0,
            LastEnqueuedAt = lastEnqueued,
            LastDequeuedAt = lastDequeued
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _queue?.Dispose();
    }

    private sealed class InternalMetrics
    {
        private long _enqueued;
        private long _dequeued;
        private long _rejected;
        private DateTimeOffset _lastEnqueuedAt = DateTimeOffset.MinValue;
        private DateTimeOffset _lastDequeuedAt = DateTimeOffset.MinValue;

        public void RecordEnqueue()
        {
            Interlocked.Increment(ref _enqueued);
            _lastEnqueuedAt = DateTimeOffset.UtcNow;
        }

        public void RecordDequeue()
        {
            Interlocked.Increment(ref _dequeued);
            _lastDequeuedAt = DateTimeOffset.UtcNow;
        }

        public void RecordRejection()
        {
            Interlocked.Increment(ref _rejected);
        }

        public (long Enqueued, long Dequeued, long Rejected, DateTimeOffset LastEnqueued, DateTimeOffset LastDequeued) GetSnapshot()
        {
            return (
                Interlocked.Read(ref _enqueued),
                Interlocked.Read(ref _dequeued),
                Interlocked.Read(ref _rejected),
                _lastEnqueuedAt,
                _lastDequeuedAt);
        }
    }
}
