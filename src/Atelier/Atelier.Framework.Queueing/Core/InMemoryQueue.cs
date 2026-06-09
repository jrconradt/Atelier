using System.Threading.Channels;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Queueing.Orchestration;

namespace Atelier.Framework.Queueing.Core;

internal sealed class InMemoryQueue : IQueue
{
    private readonly Channel<QueueMessage> _channel;
    private readonly QueueConfiguration? _configuration;
    private readonly QueueManager _parent;
    private long _lastActivityTicks = DateTime.UtcNow.Ticks;

    public string Name { get; }

    public Channel<QueueMessage> Channel => _channel;

    public InMemoryQueue(
        string name,
        Channel<QueueMessage> channel,
        QueueConfiguration? configuration,
        QueueManager parent)
    {
        Name = name;
        _channel = channel;
        _configuration = configuration;
        _parent = parent;
    }

    private static readonly TimeSpan _defaultEnqueueTimeout = TimeSpan.FromSeconds(5);

    public Task<Outcome<QueueMessage>> EnqueueAsync(
        string messageType,
        object payload,
        QueueMessageOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageType);
        ArgumentNullException.ThrowIfNull(payload);

        return EnqueueAsync(
            messageType,
            payload,
            _defaultEnqueueTimeout,
            options,
            cancellationToken);
    }

    public async Task<Outcome<QueueMessage>> EnqueueAsync(
        string messageType,
        object payload,
        TimeSpan enqueueTimeout,
        QueueMessageOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (options != null)
        {
            var unsupported = DescribeUnsupportedDelivery(options);
            if (unsupported != null)
            {
                _parent.Observe(
                    LogLevel.Warning,
                    values: [("Reason", "Delivery option not supported by in-memory FIFO queue"), ("QueueName", Name), ("Unsupported", unsupported)]);
                return Outcome<QueueMessage>.Failure();
            }
        }

        try
        {
            var message = new QueueMessage(messageType, payload);
            if (options != null)
            {
                message.MaxRetries = options.MaxRetries;
                message.CorrelationId = options.CorrelationId;
                message.TraceId = options.TraceId;
                message.SpanId = options.SpanId;
                message.ParentSpanId = options.ParentSpanId;
                message.Metadata = options.Metadata;
                message.Headers = options.Headers;
            }

            ApplicationMetrics.QueueMessagesEnqueuedTotal.WithLabels(
                Name,
                ApplicationMetrics.InstanceId,
                ApplicationMetrics.BoutiqueMode).Inc();

            if (_channel.Writer.TryWrite(message))
            {
                Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);

                return Outcome<QueueMessage>.Success(message);
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(enqueueTimeout);

            try
            {
                await _channel.Writer.WriteAsync(message, timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested
                                                     && !cancellationToken.IsCancellationRequested)
            {
                _parent.Observe(
                    LogLevel.Warning,
                    values: [("Reason", "Queue full; enqueue timed out"), ("QueueName", Name), ("TimeoutMs", enqueueTimeout.TotalMilliseconds)]);
                return Outcome<QueueMessage>.Failure();
            }

            Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);

            return Outcome<QueueMessage>.Success(message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _parent.Observe(
                LogLevel.Information,
                values: [("Reason", "Enqueue operation was cancelled"), ("QueueName", Name)]);
            return Outcome<QueueMessage>.Failure();
        }
    }

    public Task<QueueStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var (completed, failed, avgProcessingMs, inFlight) = _parent.GetQueueTelemetry(Name);

        var pendingCount = _channel.Reader.CanCount ? _channel.Reader.Count : 0;

        return Task.FromResult(new QueueStats
        {
            PendingCount = pendingCount,
            ProcessingCount = (int)inFlight,
            CompletedCount = (int)completed,
            FailedCount = (int)failed,
            AverageProcessingTimeMs = avgProcessingMs,
            LastActivity = new DateTime(Interlocked.Read(ref _lastActivityTicks), DateTimeKind.Utc)
        });
    }

    public Task<Outcome> ClearAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            _parent.Observe(
                LogLevel.Information,
                values: [("Reason", "Clear operation was cancelled"), ("QueueName", Name)]);
            return Task.FromResult(Outcome.Failure());
        }

        var drained = 0;
        while (_channel.Reader.TryRead(out _))
        {
            drained++;
        }

        if (drained > 0)
        {
            Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);
        }

        return Task.FromResult(Outcome.Success());
    }

    private static string? DescribeUnsupportedDelivery(QueueMessageOptions options)
    {
        if (options.Priority != 0)
        {
            return $"message priority (requested {options.Priority})";
        }

        if (options.ScheduledFor.HasValue)
        {
            return $"scheduled delivery (requested {options.ScheduledFor.Value:O})";
        }

        if (options.Delay.HasValue)
        {
            return $"delayed delivery (requested {options.Delay.Value})";
        }

        if (options.TimeToLiveSeconds.HasValue)
        {
            return $"time-to-live expiry (requested {options.TimeToLiveSeconds.Value}s)";
        }

        if (options.PersistToDisk)
        {
            return "durable persistence (PersistToDisk requested)";
        }

        return null;
    }
}
