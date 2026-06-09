using Atelier.Framework.Primitives;
using System.Collections.Concurrent;
using Atelier.Framework.Context;
using Atelier.Framework.EventStream.Configuration;
using Atelier.Framework.EventStream.Core;
using Atelier.Framework.EventStream.Observability;
using Atelier.Framework.EventStream.Orchestration;
using Atelier.Framework.EventStream.Storage;
using Atelier.Framework.Infrastructure;
using Atelier.Framework.Attributes;
using Atelier.Framework.Infrastructure.Operation;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;
using Microsoft.Extensions.Options;

namespace Atelier.Framework.EventStream.Consumers;

[Infrastructure(InfrastructureLifetime.Scoped)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class TopicConsumptionProcessor : IAtelier
{
    [Requisite] private readonly IEventStreamConsumer _consumer = null!;
    [Requisite] private readonly IContextAccessor _contextAccessor = null!;
    [Requisite] private readonly IEventStreamManager _streamManager = null!;
    [Requisite] private readonly IEventStreamOffsetStore _offsetStore = null!;
    [Requisite] private readonly EventStreamMetrics _metrics = null!;
    [Requisite] private readonly IOptions<EventStreamOptions> _options = null!;

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _topicCancellationSources = new();
    private readonly ConcurrentDictionary<string, Task> _topicTasks = new();
    private readonly ConcurrentDictionary<string, byte> _processedKeys = new();
    private readonly ConcurrentQueue<string> _processedKeyOrder = new();

    private const int MAX_DEDUP_KEYS = 100_000;
    private const int LIFECYCLE_IDLE = 0;
    private const int LIFECYCLE_RUNNING = 1;

    private long _eventsProcessed;
    private long _eventsFailed;
    private long _currentOffset;
    private long _lastFailureTicks;
    private int _lifecycleState;
    private DateTime? _startTime;

    public string ConsumerName => _consumer.ConsumerName;
    public string ConsumerGroup => _consumer.ConsumerGroup;

    public bool IsRunning => _startTime.HasValue
        && _topicTasks.Values.Any(t => !t.IsCompleted);

    [Operation("Start")]
    public async Task<Outcome> StartAsync(CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return Outcome.Failure();
        }

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "Consumer", ConsumerName);

        if (Interlocked.CompareExchange(ref _lifecycleState, LIFECYCLE_RUNNING, LIFECYCLE_IDLE) != LIFECYCLE_IDLE)
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", "Consumer is already running"), ("ConsumerName", ConsumerName)]);
            return Outcome.Failure();
        }

        _metrics.SetWorkerStatus(ConsumerName, WorkerState.Starting);
        _startTime = DateTime.UtcNow;

        var topics = _consumer.Topics as IReadOnlyList<string> ?? _consumer.Topics.ToList();

        foreach (var topic in topics)
        {
            if (_topicTasks.ContainsKey(topic))
            {
                continue;
            }

            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (!_topicCancellationSources.TryAdd(topic, cts))
            {
                cts.Dispose();
                continue;
            }

            _topicTasks[topic] = RunTopicAsync(topic, cts.Token);
        }

        _metrics.SetWorkerStatus(ConsumerName, WorkerState.Running);

        Observe(LogLevel.Information, values: [("ConsumerName", ConsumerName), ("ConsumerGroup", ConsumerGroup), ("TopicCount", topics.Count)]);

        return Outcome.Success();
    }

    [Operation("Stop")]
    public async Task<Outcome> StopAsync(CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return Outcome.Failure();
        }

        if (Interlocked.CompareExchange(ref _lifecycleState, LIFECYCLE_IDLE, LIFECYCLE_RUNNING) != LIFECYCLE_RUNNING)
        {
            return Outcome.Success();
        }

        _metrics.SetWorkerStatus(ConsumerName, WorkerState.Stopping);

        foreach (var cts in _topicCancellationSources.Values)
        {
            cts.Cancel();
        }

        var tasks = _topicTasks.Values.ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);

        foreach (var cts in _topicCancellationSources.Values)
        {
            cts.Dispose();
        }

        _topicCancellationSources.Clear();
        _topicTasks.Clear();
        _startTime = null;

        _metrics.SetWorkerStatus(ConsumerName, WorkerState.Stopped);

        Observe(LogLevel.Information, values: [("ConsumerName", ConsumerName)]);

        return Outcome.Success();
    }

    [Operation("Decommission")]
    public async Task<Outcome> DecommissionAsync(CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return Outcome.Failure();
        }

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "Consumer", ConsumerName);

        var stopResult = await StopAsync(ct).ConfigureAwait(false);
        if (!stopResult.IsSuccess)
        {
            return stopResult;
        }

        _processedKeys.Clear();
        while (_processedKeyOrder.TryDequeue(out _))
        {
        }

        var deleteResult = await _offsetStore.DeleteOffsetsForConsumerAsync(ConsumerGroup, ct).ConfigureAwait(false);
        if (!deleteResult.IsSuccess)
        {
            Observe(LogLevel.Error, values: [("Reason", "Failed to delete offsets during decommission"), ("ConsumerName", ConsumerName), ("ConsumerGroup", ConsumerGroup)]);

            return Outcome.Failure();
        }

        Observe(LogLevel.Information, values: [("ConsumerName", ConsumerName), ("ConsumerGroup", ConsumerGroup), ("DeletedOffsets", deleteResult.Data)]);

        return Outcome.Success();
    }

    public ConsumerStats GetStats()
    {
        var lastFailureTicks = Interlocked.Read(ref _lastFailureTicks);

        return new ConsumerStats
        {
            ConsumerName = ConsumerName,
            ConsumerGroup = ConsumerGroup,
            EventsProcessed = Interlocked.Read(ref _eventsProcessed),
            EventsFailed = Interlocked.Read(ref _eventsFailed),
            CurrentOffset = Interlocked.Read(ref _currentOffset),
            Lag = 0,
            EventsPerSecond = 0.0,
            Uptime = _startTime.HasValue ? DateTime.UtcNow - _startTime.Value : TimeSpan.Zero,
            LastFailureUtc = lastFailureTicks == 0 ? null : new DateTime(lastFailureTicks, DateTimeKind.Utc)
        };
    }

    private async Task RunTopicAsync(string topic, CancellationToken ct)
    {
        var tuning = _options.Value;
        var maxRestarts = tuning.ConsumerMaxRestarts;
        var baseBackoff = TimeSpan.FromMilliseconds(tuning.ConsumerRestartBackoffMs);
        var maxBackoff = TimeSpan.FromMilliseconds(tuning.ConsumerRestartBackoffMaxMs);
        var restarts = 0;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await RunTopicAttemptAsync(topic, ct).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _metrics.SetWorkerStatus(ConsumerName, WorkerState.Error);

                    if (restarts >= maxRestarts)
                    {
                        Observe(LogLevel.Error, ex, values: [("Topic", topic), ("ConsumerName", ConsumerName), ("Restarts", restarts), ("Action", "SUPERVISOR_GIVE_UP")]);
                        return;
                    }

                    var delay = ComputeRestartBackoff(baseBackoff, maxBackoff, restarts);
                    restarts++;

                    Observe(LogLevel.Warning, ex, values: [("Topic", topic), ("ConsumerName", ConsumerName), ("Restart", restarts), ("BackoffMs", (long)delay.TotalMilliseconds), ("Action", "SUPERVISOR_RESTART")]);

                    try
                    {
                        await Task.Delay(delay, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }
        finally
        {
            _topicTasks.TryRemove(topic, out _);

            if (!_topicTasks.Values.Any(t => !t.IsCompleted))
            {
                if (Interlocked.CompareExchange(ref _lifecycleState, LIFECYCLE_IDLE, LIFECYCLE_RUNNING) == LIFECYCLE_RUNNING)
                {
                    _startTime = null;
                    _metrics.SetWorkerStatus(ConsumerName, WorkerState.Stopped);
                }
            }
        }
    }

    private async Task RunTopicAttemptAsync(string topic, CancellationToken ct)
    {
        var stream = await GetStreamAsync(topic, ct).ConfigureAwait(false);
        if (stream == null)
        {
            return;
        }

        var fromOffset = await GetStartingOffsetAsync(ConsumerGroup, topic, ct).ConfigureAwait(false);

        Observe(LogLevel.Information, values: [("ConsumerName", ConsumerName), ("Topic", topic), ("FromOffset", fromOffset)]);

        await ProcessEventLoopAsync(stream, topic, ConsumerGroup, fromOffset, ct).ConfigureAwait(false);
    }

    private static TimeSpan ComputeRestartBackoff(TimeSpan baseBackoff, TimeSpan maxBackoff, int restarts)
    {
        var scaled = baseBackoff.TotalMilliseconds * Math.Pow(2, restarts);
        var capped = Math.Min(scaled, maxBackoff.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(capped);
    }

    private async Task<IEventStream?> GetStreamAsync(string topic, CancellationToken ct)
    {
        var streamResult = await _streamManager.GetStreamAsync(topic, ct).ConfigureAwait(false);

        if (!streamResult.IsSuccess)
        {
            Observe(LogLevel.Error, values: [("Reason", "Failed to acquire stream for topic"), ("Topic", topic)]);
            return null;
        }

        return streamResult.Data;
    }

    private async Task<long> GetStartingOffsetAsync(string consumerGroup, string topic, CancellationToken ct)
    {
        var offsetResult = await _offsetStore.GetOffsetAsync(consumerGroup, topic, ct).ConfigureAwait(false);
        return offsetResult.IsSuccess ? offsetResult.Data : 0L;
    }

    private async Task ProcessEventLoopAsync(
        IEventStream stream,
        string topic,
        string consumerGroup,
        long fromOffset,
        CancellationToken ct)
    {
        var tuning = _options.Value;
        var batchSize = tuning.ConsumerBatchSize;
        var commitInterval = tuning.ConsumerCommitInterval;
        var maxRetries = tuning.ConsumerMaxRetries;
        var idlePollDelay = TimeSpan.FromMilliseconds(tuning.ConsumerIdlePollDelayMs);
        var errorBackoff = TimeSpan.FromMilliseconds(tuning.ConsumerErrorBackoffMs);
        var processedSinceCommit = 0;
        var currentOffset = fromOffset;
        var lastCommittedOffset = fromOffset;
        var retryOffset = long.MinValue;
        var retryAttempts = 0;

        while (!ct.IsCancellationRequested)
        {
            var hasProcessedEvents = false;
            var deferred = false;
            var uncommittedOffset = currentOffset;

            await foreach (var streamEvent in stream.ReadAsync(topic, currentOffset, batchSize, ct))
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                hasProcessedEvents = true;

                if (IsDuplicate(topic, streamEvent))
                {
                    Interlocked.Exchange(ref _currentOffset, streamEvent.Offset);
                    currentOffset = streamEvent.Offset + 1;
                    uncommittedOffset = currentOffset;
                    continue;
                }

                var result = await ProcessSingleEventAsync(streamEvent, topic, ct).ConfigureAwait(false);

                if (result.IsSuccess)
                {
                    MarkProcessed(topic, streamEvent);
                    Interlocked.Exchange(ref _currentOffset, streamEvent.Offset);
                    currentOffset = streamEvent.Offset + 1;
                    uncommittedOffset = currentOffset;
                    retryOffset = long.MinValue;
                    retryAttempts = 0;
                    processedSinceCommit++;

                    if (processedSinceCommit >= commitInterval)
                    {
                        if (await TryCommitAsync(consumerGroup, topic, uncommittedOffset, ct).ConfigureAwait(false))
                        {
                            lastCommittedOffset = uncommittedOffset;
                        }
                        processedSinceCommit = 0;
                    }

                    continue;
                }

                if (streamEvent.Offset != retryOffset)
                {
                    retryOffset = streamEvent.Offset;
                    retryAttempts = 0;
                }

                retryAttempts++;

                if (retryAttempts > maxRetries)
                {
                    Observe(LogLevel.Error, values: [("Topic", topic), ("Offset", streamEvent.Offset), ("Attempts", retryAttempts - 1), ("Action", "POISON_SKIP")]);

                    MarkProcessed(topic, streamEvent);
                    Interlocked.Exchange(ref _currentOffset, streamEvent.Offset);
                    currentOffset = streamEvent.Offset + 1;
                    uncommittedOffset = currentOffset;
                    retryOffset = long.MinValue;
                    retryAttempts = 0;
                    processedSinceCommit++;

                    if (processedSinceCommit >= commitInterval)
                    {
                        if (await TryCommitAsync(consumerGroup, topic, uncommittedOffset, ct).ConfigureAwait(false))
                        {
                            lastCommittedOffset = uncommittedOffset;
                        }
                        processedSinceCommit = 0;
                    }

                    continue;
                }

                deferred = true;
                break;
            }

            if (processedSinceCommit > 0
                && uncommittedOffset > lastCommittedOffset)
            {
                if (await TryCommitAsync(consumerGroup, topic, uncommittedOffset, ct).ConfigureAwait(false))
                {
                    lastCommittedOffset = uncommittedOffset;
                }
                processedSinceCommit = 0;
            }

            if (deferred)
            {
                await Task.Delay(errorBackoff, ct).ConfigureAwait(false);
                continue;
            }

            if (!hasProcessedEvents)
            {
                await Task.Delay(idlePollDelay, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task<Outcome> ProcessSingleEventAsync(
        StreamEvent streamEvent,
        string topic,
        CancellationToken ct)
    {
        var schemaVersion = streamEvent.Metadata?.SchemaVersion ?? EventMetadata.CURRENT_SCHEMA_VERSION;
        if (schemaVersion > EventMetadata.CURRENT_SCHEMA_VERSION)
        {
            Interlocked.Increment(ref _eventsFailed);
            Interlocked.Exchange(ref _lastFailureTicks, DateTime.UtcNow.Ticks);
            _metrics.RecordMessageFailed(ConsumerName, topic, "SchemaVersionUnsupported");
            _metrics.SetWorkerStatus(ConsumerName, WorkerState.Error);

            Observe(LogLevel.Error, values: [("Reason", "Event schema version exceeds supported version"), ("Topic", topic), ("Offset", streamEvent.Offset), ("SchemaVersion", schemaVersion), ("SupportedSchemaVersion", EventMetadata.CURRENT_SCHEMA_VERSION)]);

            return Outcome.Failure();
        }

        Outcome result;

        var eventContext = new CompositeContext(
            Guid.NewGuid().ToString(),
            $"StreamEvent-{topic}-{streamEvent.Offset}",
            null);
        eventContext.AdoptParentSpan(
            streamEvent.Metadata?.TraceId,
            streamEvent.Metadata?.SpanId ?? streamEvent.Metadata?.ParentSpanId,
            streamEvent.Metadata?.CorrelationId);
        _contextAccessor.SetCurrent(eventContext);

        using (_metrics.MeasureProcessingDuration(ConsumerName, topic))
        {
            try
            {
                result = await _consumer.ProcessEventAsync(streamEvent, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Observe(LogLevel.Error, ex, values: [("Reason", "Consumer threw while processing event"), ("ConsumerName", ConsumerName), ("Topic", topic), ("Offset", streamEvent.Offset)]);
                result = Outcome.Failure();
            }
            finally
            {
                _contextAccessor.SetCurrent(null!);
            }
        }

        if (result.IsSuccess)
        {
            Interlocked.Increment(ref _eventsProcessed);
            _metrics.RecordMessageProcessed(ConsumerName, topic);
        }
        else
        {
            Interlocked.Increment(ref _eventsFailed);
            Interlocked.Exchange(ref _lastFailureTicks, DateTime.UtcNow.Ticks);
            _metrics.RecordMessageFailed(ConsumerName, topic, "ProcessingFailed");
            _metrics.SetWorkerStatus(ConsumerName, WorkerState.Error);

            Observe(LogLevel.Warning, values: [("Reason", "Consumer reported event processing failure"), ("Topic", topic), ("Offset", streamEvent.Offset)]);
        }

        return result;
    }

    private async Task<bool> TryCommitAsync(string consumerGroup, string topic, long offset, CancellationToken ct)
    {
        var commitResult = await _offsetStore.CommitOffsetAsync(consumerGroup, topic, offset, ct).ConfigureAwait(false);

        if (!commitResult.IsSuccess)
        {
            Observe(LogLevel.Error, values: [("Reason", "Offset commit failed"), ("ConsumerGroup", consumerGroup), ("Topic", topic), ("Offset", offset)]);
        }

        return commitResult.IsSuccess;
    }

    private static string DedupKey(string topic, StreamEvent streamEvent)
    {
        var correlationId = streamEvent.Metadata?.CorrelationId;
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            return $"{topic}:cid:{correlationId}";
        }

        return $"{topic}:off:{streamEvent.Offset}";
    }

    private bool IsDuplicate(string topic, StreamEvent streamEvent)
    {
        return _processedKeys.ContainsKey(DedupKey(topic, streamEvent));
    }

    private void MarkProcessed(string topic, StreamEvent streamEvent)
    {
        var key = DedupKey(topic, streamEvent);
        if (!_processedKeys.TryAdd(key, 0))
        {
            return;
        }

        _processedKeyOrder.Enqueue(key);

        while (_processedKeyOrder.Count > MAX_DEDUP_KEYS
            && _processedKeyOrder.TryDequeue(out var oldest))
        {
            _processedKeys.TryRemove(oldest, out _);
        }
    }
}
