using System.Collections.Concurrent;
using System.Threading.Channels;
using Atelier.Framework.Context;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Queueing.Attributes;
using Atelier.Framework.Queueing.Core;
using Atelier.Framework.Queueing.Orchestration;
using Atelier.Framework.Requisitions;

namespace Atelier.Framework.Queueing.Workers;

public abstract partial class QueueWorkerBase : IAtelier, IQueueWorker, IDisposable
{
    [Requisite] protected readonly IQueueManager _queueManager = null!;

    private readonly ConcurrentDictionary<string, Channel<QueueMessage>> _activeChannels = new();
    private CancellationTokenSource _cancellationTokenSource = new();
    private CancellationTokenSource? _linkedTokenSource;
    private readonly ConcurrentDictionary<Task, byte> _processingTasks = new();
    private readonly ConcurrentDictionary<string, int> _queueRetryAttempts = new();
    private readonly ConcurrentDictionary<string, HandlerDispatch> _handlerDispatchCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Task, byte> _retryTasks = new();
    private readonly ConcurrentDictionary<string, byte> _processedMessageIds = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _processedMessageIdOrder = new();
    private int _retrySlotsInUse;
    private readonly Dictionary<string, MessageHandlerConfiguration> _handlerByType = new(StringComparer.OrdinalIgnoreCase);
    private MessageHandlerConfiguration? _catchAllHandler;
    private string _primaryQueueName = "unknown";
    private int _status = (int)WorkerStatus.Stopped;
    private string? _lastError;
    private long _messagesProcessed;
    private long _messagesFailed;
    private long _totalProcessingTimeMicros;
    private DateTime _startedAt = DateTime.UtcNow;
    private long _lastProcessedAtTicks = DateTime.UtcNow.Ticks;

    protected QueueWorkerBase() { }

    private const int INITIAL_DELAY_MS = 100;
    private const int MAX_DELAY_MS = 30000;
    private const double BACKOFF_MULTIPLIER = 2.0;
    private const double JITTER_PERCENT = 0.25;
    private const int MAX_CONCURRENT_RETRIES = 256;
    private const int MAX_DEDUP_KEYS = 100_000;

        public abstract string WorkerName { get; }

        public abstract IEnumerable<QueueWorkerConfiguration> QueueConfigurations { get; }

        public abstract IEnumerable<MessageHandlerConfiguration> MessageHandlerConfigurations { get; }

        public WorkerStatus Status => (WorkerStatus)Volatile.Read(ref _status);

    private void SetStatus(WorkerStatus status)
    {
        Volatile.Write(ref _status, (int)status);
    }

    private bool TrySetStatus(WorkerStatus expected, WorkerStatus desired)
    {
        return Interlocked.CompareExchange(ref _status, (int)desired, (int)expected) == (int)expected;
    }

    private void RecordError(string message)
    {
        SetLastError(message);
        SetStatus(WorkerStatus.Error);
    }

    private void SetLastError(string message)
    {
        Volatile.Write(ref _lastError, message);
    }

    private void SnapshotConfigurations()
    {
        _handlerByType.Clear();
        _catchAllHandler = null;

        foreach (var handler in MessageHandlerConfigurations)
        {
            if (handler.HandleAllTypes)
            {
                _catchAllHandler ??= handler;
            }
            else
            {
                _handlerByType[handler.MessageType] = handler;
            }
        }

        _primaryQueueName = QueueConfigurations.FirstOrDefault()?.QueueName ?? "unknown";
    }

    private MessageHandlerConfiguration? ResolveHandlerConfig(string messageType)
    {
        if (_handlerByType.TryGetValue(messageType, out var handler))
        {
            return handler;
        }

        return _catchAllHandler;
    }

        public virtual async Task<Outcome> StartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!TrySetStatus(WorkerStatus.Stopped, WorkerStatus.Starting))
            {
                return Outcome.Success();
            }

            _startedAt = DateTime.UtcNow;

            var validationResult = ValidateConfigurations();
            if (!validationResult.IsSuccess)
            {
                SetStatus(WorkerStatus.Error);
                return validationResult;
            }

            SnapshotConfigurations();

            _cancellationTokenSource = new CancellationTokenSource();
            _linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _cancellationTokenSource.Token);
            var linkedToken = _linkedTokenSource.Token;

            foreach (var queueConfig in QueueConfigurations)
            {
                var concurrency = Math.Max(1, queueConfig.MaxConcurrency);
                for (var worker = 0; worker < concurrency; worker++)
                {
                    var processingTask = StartQueueProcessingAsync(queueConfig, linkedToken);
                    _processingTasks.TryAdd(processingTask, 0);
                }
            }

            SetStatus(WorkerStatus.Running);

            Logger?
                .WithContextMetadata()
                .WithMessage("Queue worker started successfully")
                .WithValue("WorkerName", WorkerName)
                .WithValue("QueueCount", QueueConfigurations.Count())
                .WithValue("AttachedChannels", _activeChannels.Count)
                .WithLevel(LogLevel.Information)
                .Log();

            return Outcome.Success();
        }
        catch (Exception ex)
        {
            RecordError(ex.Message);
            Observe(
                LogLevel.Error,
                ex,
                values: [("Reason", "Failed to start queue worker"), ("WorkerName", WorkerName)]);
            return Outcome.Failure();
        }
    }

        public virtual async Task<Outcome> StopAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var current = Status;
            if (current == WorkerStatus.Stopped
                || current == WorkerStatus.Stopping)
            {
                return Outcome.Success();
            }

            SetStatus(WorkerStatus.Stopping);

            _cancellationTokenSource.Cancel();

            var outstanding = _processingTasks.Keys.Concat(_retryTasks.Keys).ToList();

            if (outstanding.Count > 0)
            {
                try
                {
                    await Task.WhenAll(outstanding).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            foreach (var channel in _activeChannels.Values)
            {
                channel.Writer.Complete();
            }

            _activeChannels.Clear();
            _processingTasks.Clear();
            _retryTasks.Clear();

            _linkedTokenSource?.Dispose();
            _linkedTokenSource = null;
            _cancellationTokenSource.Dispose();

            SetStatus(WorkerStatus.Stopped);

            Logger?
                .WithContextMetadata()
                .WithMessage("Queue worker stopped successfully")
                .WithValue("WorkerName", WorkerName)
                .WithValue("MessagesProcessed", _messagesProcessed)
                .WithValue("MessagesFailed", _messagesFailed)
                .WithLevel(LogLevel.Information)
                .Log();

            return Outcome.Success();
        }
        catch (Exception ex)
        {
            RecordError(ex.Message);
            Observe(
                LogLevel.Error,
                ex,
                values: [("Reason", "Failed to stop queue worker"), ("WorkerName", WorkerName)]);
            return Outcome.Failure();
        }
    }

        public WorkerStats GetStats()
    {
        var processed = Interlocked.Read(ref _messagesProcessed);
        var failed = Interlocked.Read(ref _messagesFailed);
        var totalCount = processed + failed;
        var totalMs = Interlocked.Read(ref _totalProcessingTimeMicros) / 1000.0;
        var averageMs = totalCount > 0 ? totalMs / totalCount : 0.0;
        var lastProcessedAt = new DateTime(Interlocked.Read(ref _lastProcessedAtTicks), DateTimeKind.Utc);
        var lastError = Volatile.Read(ref _lastError);

        return new WorkerStats
        {
            Status = Status,
            MessagesProcessed = processed,
            MessagesFailed = failed,
            MessagesInProgress = _processingTasks.Keys.Count(t => !t.IsCompleted),
            AverageProcessingTimeMs = averageMs,
            LastProcessedAt = lastProcessedAt,
            StartedAt = _startedAt,
            LastError = lastError
        };
    }

    private void IncDequeued(string queueName)
    {
        ApplicationMetrics.QueueMessagesDequeuedTotal.WithLabels(
            queueName,
            ApplicationMetrics.InstanceId,
            ApplicationMetrics.BoutiqueMode).Inc();
    }

    private void RecordOutcome(string queueName,
                               bool success,
                               double processingTimeMs)
    {
        UpdateProcessingStats(success, processingTimeMs);

        ApplicationMetrics.QueueProcessingDuration.WithLabels(
            queueName,
            ApplicationMetrics.InstanceId,
            ApplicationMetrics.BoutiqueMode).Observe(processingTimeMs / 1000.0);

        if (success)
        {
            ApplicationMetrics.QueueMessagesProcessedTotal.WithLabels(
                queueName,
                ApplicationMetrics.InstanceId,
                ApplicationMetrics.BoutiqueMode).Inc();
        }
        else
        {
            ApplicationMetrics.QueueMessagesFailedTotal.WithLabels(
                queueName,
                ApplicationMetrics.InstanceId,
                ApplicationMetrics.BoutiqueMode).Inc();
        }
    }

        protected virtual async Task<Outcome> ProcessMessageAsync(
        QueueMessage message,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var queueName = _primaryQueueName;

        IncDequeued(queueName);

        try
        {
            if (_processedMessageIds.ContainsKey(message.Id))
            {
                var dedupTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                RecordOutcome(queueName, true, dedupTime);

                Logger?
                    .WithContextMetadata()
                    .WithMessage("Skipped already-processed message (idempotency dedup)")
                    .WithValue("MessageId", message.Id)
                    .WithValue("MessageType", message.MessageType)
                    .WithLevel(LogLevel.Debug)
                    .Log();

                return Outcome.Success();
            }

            var messageContext = new global::Atelier.Framework.Context.Context(
                Guid.NewGuid().ToString(),
                $"QueueMessage-{message.Id}",
                null);
            messageContext.TraceId = message.TraceId;
            messageContext.ParentSpanId = message.SpanId ?? message.ParentSpanId;
            if (message.CorrelationId != null)
            {
                messageContext.CorrelationId = message.CorrelationId;
            }
            messageContext.AddValue("MessageId", message.Id);
            messageContext.AddValue("MessageType", message.MessageType);
            messageContext.AddValue("WorkerName", WorkerName);

            if (message.Metadata != null && message.Metadata.Count > 0)
            {
                foreach (var kvp in message.Metadata.GetAll())
                {
                    messageContext.AddValue($"Queue.{kvp.Key}", kvp.Value.ToString() ?? string.Empty);
                }
            }

            AmbientContext.SetCurrent(messageContext);

            var handlerConfig = ResolveHandlerConfig(message.MessageType);

            if (handlerConfig == null)
            {
                Observe(
                    LogLevel.Warning,
                    values: [("Reason", "No handler found for message type"), ("MessageId", message.Id), ("MessageType", message.MessageType)]);
                return Outcome.Failure();
            }

            var dispatch = GetHandlerDispatch(handlerConfig.MessageType);
            if (!dispatch.HasHandler)
            {
                Observe(
                    LogLevel.Warning,
                    values: [("Reason", "Handler method not found for message type"), ("MessageId", message.Id), ("MessageType", message.MessageType)]);
                return Outcome.Failure();
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(handlerConfig.TimeoutMs);

            var result = await ExecuteHandlerAsync(dispatch, message, timeoutCts.Token).ConfigureAwait(false);

            var processingTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

            RecordOutcome(queueName, result.IsSuccess, processingTime);

            if (result.IsSuccess)
            {
                MarkProcessed(message.Id);


                Logger?
                    .WithContextMetadata()
                    .WithMessage("Successfully processed message")
                    .WithValue("MessageId", message.Id)
                    .WithValue("MessageType", message.MessageType)
                    .WithValue("ProcessingTimeMs", processingTime)
                    .WithLevel(LogLevel.Debug)
                    .Log();
            }
            else
            {
                Logger?
                    .WithContextMetadata()
                    .WithMessage("Failed to process message")
                    .WithValue("MessageId", message.Id)
                    .WithValue("MessageType", message.MessageType)
                    .WithValue("ProcessingTimeMs", processingTime)
                    .WithLevel(LogLevel.Warning)
                    .Log();
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            var processingTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
            RecordOutcome(queueName, false, processingTime);

            Logger?
                .WithContextMetadata()
                .WithMessage("Message processing cancelled or timed out")
                .WithValue("MessageId", message.Id)
                .WithValue("MessageType", message.MessageType)
                .WithValue("ProcessingTimeMs", processingTime)
                .WithLevel(LogLevel.Warning)
                .Log();

            return Outcome.Failure();
        }
        catch (Exception ex)
        {
            var processingTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
            RecordOutcome(queueName, false, processingTime);
            SetLastError(ex.Message);

            Logger?
                .WithContextMetadata()
                .WithMessage("Unexpected error processing message")
                .WithValue("MessageId", message.Id)
                .WithValue("MessageType", message.MessageType)
                .WithError(ex)
                .WithLevel(LogLevel.Error)
                .Log();

            return Outcome.Failure();
        }
        finally
        {
            AmbientContext.SetCurrent(null!);
        }
    }

    private void MarkProcessed(string messageId)
    {
        if (!_processedMessageIds.TryAdd(messageId, 0))
        {
            return;
        }

        _processedMessageIdOrder.Enqueue(messageId);

        while (_processedMessageIdOrder.Count > MAX_DEDUP_KEYS
            && _processedMessageIdOrder.TryDequeue(out var oldest))
        {
            _processedMessageIds.TryRemove(oldest, out _);
        }
    }

        protected virtual Outcome ValidateConfigurations()
    {
        if (string.IsNullOrWhiteSpace(WorkerName))
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", "Worker name was null or empty")]);
            return Outcome.Failure();
        }

        if (!QueueConfigurations.Any())
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", "At least one queue configuration is required"), ("WorkerName", WorkerName)]);
            return Outcome.Failure();
        }

        if (!MessageHandlerConfigurations.Any())
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", "At least one message handler configuration is required"), ("WorkerName", WorkerName)]);
            return Outcome.Failure();
        }

        return Outcome.Success();
    }

        private void UpdateProcessingStats(bool success, double processingTimeMs)
    {
        if (success)
        {
            Interlocked.Increment(ref _messagesProcessed);
        }
        else
        {
            Interlocked.Increment(ref _messagesFailed);
        }

        Interlocked.Exchange(ref _lastProcessedAtTicks, DateTime.UtcNow.Ticks);

        Interlocked.Add(ref _totalProcessingTimeMicros, (long)Math.Round(processingTimeMs * 1000.0));
    }

        private int CalculateBackoffDelay(string queueName, bool incrementAttempt = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);

        int currentAttempt;
        if (incrementAttempt)
        {
            currentAttempt = _queueRetryAttempts.AddOrUpdate(queueName, 1, (_, v) => v + 1) - 1;
        }
        else
        {
            currentAttempt = _queueRetryAttempts.GetOrAdd(queueName, 0);
        }

        var baseDelay = INITIAL_DELAY_MS * Math.Pow(BACKOFF_MULTIPLIER, currentAttempt);

        var cappedDelay = Math.Min(baseDelay, MAX_DELAY_MS);

        var jitterRange = cappedDelay * JITTER_PERCENT;
        var jitter = (Random.Shared.NextDouble() * 2 - 1) * jitterRange;
        var finalDelay = cappedDelay + jitter;

        return (int)Math.Max(finalDelay, INITIAL_DELAY_MS);
    }

        private void ResetBackoff(string queueName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);

        _queueRetryAttempts.TryRemove(queueName, out _);
    }

        private int CalculateMessageRetryDelay(int attempt, int baseDelayMs)
    {
        var safeBase = Math.Max(baseDelayMs, INITIAL_DELAY_MS);
        var baseDelay = safeBase * Math.Pow(BACKOFF_MULTIPLIER, Math.Max(0, attempt));
        var cappedDelay = Math.Min(baseDelay, MAX_DELAY_MS);

        var jitterRange = cappedDelay * JITTER_PERCENT;
        var jitter = (Random.Shared.NextDouble() * 2 - 1) * jitterRange;
        var finalDelay = cappedDelay + jitter;

        return (int)Math.Max(finalDelay, INITIAL_DELAY_MS);
    }

        private void ScheduleRetry(
        Channel<QueueMessage> channel,
        QueueMessage message,
        QueueWorkerConfiguration config,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(config);

        var retryDelayMs = CalculateMessageRetryDelay(message.RetryCount, config.RetryDelayMs);
        var retryMessage = message.CreateRetry();

        if (Interlocked.Increment(ref _retrySlotsInUse) > MAX_CONCURRENT_RETRIES)
        {
            Interlocked.Decrement(ref _retrySlotsInUse);
            _ = RouteToDeadLetterAsync(retryMessage, config, "RETRY_BACKPRESSURE", cancellationToken);
            return;
        }

        Task? retryTask = null;
        retryTask = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(retryDelayMs, cancellationToken).ConfigureAwait(false);

                if (!channel.Writer.TryWrite(retryMessage))
                {
                    await RouteToDeadLetterAsync(retryMessage, config, "RETRY_CHANNEL_FULL", cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Logger?
                    .WithMessage("Failed to schedule message retry")
                    .WithValue("MessageId", retryMessage.Id)
                    .WithValue("QueueName", config.QueueName)
                    .WithError(ex)
                    .WithLevel(LogLevel.Error)
                    .Log();
            }
            finally
            {
                Interlocked.Decrement(ref _retrySlotsInUse);

                var self = Volatile.Read(ref retryTask);
                if (self != null)
                {
                    _retryTasks.TryRemove(self, out _);
                }
            }
        }, cancellationToken);

        _retryTasks.TryAdd(retryTask, 0);
    }

        private async Task RouteToDeadLetterAsync(
        QueueMessage message,
        QueueWorkerConfiguration config,
        string reason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(reason);

        var queueName = config.QueueName;

        ApplicationMetrics.QueueMessagesFailedTotal.WithLabels(
            queueName,
            ApplicationMetrics.InstanceId,
            ApplicationMetrics.BoutiqueMode).Inc();

        if (!config.EnableDeadLetterQueue)
        {
            Logger?
                .WithContextMetadata()
                .WithMessage("Message exhausted retries and dropped (no dead-letter queue configured)")
                .WithValue("MessageId", message.Id)
                .WithValue("MessageType", message.MessageType)
                .WithValue("QueueName", queueName)
                .WithValue("RetryCount", message.RetryCount)
                .WithValue("Reason", reason)
                .WithLevel(LogLevel.Error)
                .Log();

            return;
        }

        var deadLetterQueueName = !string.IsNullOrWhiteSpace(config.DeadLetterQueueName)
            ? config.DeadLetterQueueName
            : $"{queueName}-dead-letter";

        try
        {
            var queueResult = await _queueManager.GetQueueAsync(deadLetterQueueName, cancellationToken).ConfigureAwait(false);
            if (!queueResult.IsSuccess)
            {
                Logger?
                    .WithContextMetadata()
                    .WithMessage("Failed to resolve dead-letter queue; message dropped")
                    .WithValue("MessageId", message.Id)
                    .WithValue("MessageType", message.MessageType)
                    .WithValue("QueueName", queueName)
                    .WithValue("DeadLetterQueueName", deadLetterQueueName)
                    .WithLevel(LogLevel.Error)
                    .Log();

                return;
            }

            if (!queueResult.Data!.Channel.Writer.TryWrite(message))
            {
                Logger?
                    .WithContextMetadata()
                    .WithMessage("Dead-letter queue full; message dropped")
                    .WithValue("MessageId", message.Id)
                    .WithValue("MessageType", message.MessageType)
                    .WithValue("DeadLetterQueueName", deadLetterQueueName)
                    .WithLevel(LogLevel.Error)
                    .Log();

                return;
            }

            Logger?
                .WithContextMetadata()
                .WithMessage("Message routed to dead-letter queue after retry exhaustion")
                .WithValue("MessageId", message.Id)
                .WithValue("MessageType", message.MessageType)
                .WithValue("QueueName", queueName)
                .WithValue("DeadLetterQueueName", deadLetterQueueName)
                .WithValue("RetryCount", message.RetryCount)
                .WithValue("Reason", reason)
                .WithLevel(LogLevel.Error)
                .Log();
        }
        catch (OperationCanceledException)
        {
            Logger?
                .WithContextMetadata()
                .WithMessage("Dead-letter routing cancelled during shutdown; message dropped")
                .WithValue("MessageId", message.Id)
                .WithValue("MessageType", message.MessageType)
                .WithValue("QueueName", queueName)
                .WithValue("DeadLetterQueueName", deadLetterQueueName)
                .WithLevel(LogLevel.Warning)
                .Log();
        }
        catch (Exception ex)
        {
            Logger?
                .WithContextMetadata()
                .WithMessage("Error routing message to dead-letter queue; message dropped")
                .WithValue("MessageId", message.Id)
                .WithValue("MessageType", message.MessageType)
                .WithValue("DeadLetterQueueName", deadLetterQueueName)
                .WithError(ex)
                .WithLevel(LogLevel.Error)
                .Log();
        }
    }

    private async Task StartQueueProcessingAsync(QueueWorkerConfiguration config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);

        await Task.Yield();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!_activeChannels.TryGetValue(config.QueueName, out var channel))
                {
                    var backoffDelay = CalculateBackoffDelay(config.QueueName);
                    Logger?.WithMessage("Channel for queue not found, retrying with backoff")
                           .WithValue("QueueName", config.QueueName)
                           .WithValue("BackoffDelayMs", backoffDelay)
                           .WithLevel(LogLevel.Warning)
                           .Log();
                    await Task.Delay(backoffDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                ResetBackoff(config.QueueName);

                var message = await channel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);

                _queueManager.RecordMessageRead(config.QueueName);

                var startTime = DateTime.UtcNow;
                var result = await ProcessMessageAsync(message, cancellationToken).ConfigureAwait(false);
                var processingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;

                _queueManager.RecordProcessingResult(config.QueueName, result.IsSuccess, processingTimeMs);

                if (!result.IsSuccess)
                {
                    var canRetry = message.RetryCount < message.MaxRetries
                                   && config.MaxRetries > 0
                                   && message.RetryCount < config.MaxRetries;

                    if (canRetry)
                    {
                        ScheduleRetry(channel, message, config, cancellationToken);
                    }
                    else
                    {
                        await RouteToDeadLetterAsync(message, config, "RETRY_EXHAUSTED", cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                SetLastError(ex.Message);

                var backoffDelay = CalculateBackoffDelay($"{config.QueueName}_error");
                Logger?.WithMessage($"Error in queue processing loop for queue '{config.QueueName}', retrying with backoff")
                       .WithValue("QueueName", config.QueueName)
                       .WithValue("BackoffDelayMs", backoffDelay)
                       .WithError(ex)
                       .WithLevel(LogLevel.Error)
                       .Log();

                await Task.Delay(backoffDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private HandlerDispatch GetHandlerDispatch(string messageType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);

        return _handlerDispatchCache.GetOrAdd(messageType, mt => HandlerDispatchCompiler.Build(GetType(), mt));
    }

    private async Task<Outcome> ExecuteHandlerAsync(
        HandlerDispatch dispatch,
        QueueMessage message,
        CancellationToken cancellationToken)
    {
        if (dispatch.BuildError != null)
        {
            Observe(
                LogLevel.Error,
                values: [("Reason", "Handler dispatch build error"), ("MessageId", message.Id), ("MessageType", message.MessageType), ("BuildError", dispatch.BuildError)]);
            return Outcome.Failure();
        }

        var inputType = dispatch.InputType!;
        object? input;

        try
        {
            input = dispatch.Deserialize!(message);
        }
        catch (Exception ex)
        {
            Observe(
                LogLevel.Error,
                ex,
                values: [("Reason", "Failed to deserialize message payload"), ("MessageId", message.Id), ("InputType", inputType.Name)]);
            return Outcome.Failure();
        }

        if (input == null)
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", "Deserialized message payload was null"), ("MessageId", message.Id), ("InputType", inputType.Name)]);
            return Outcome.Failure();
        }

        var task = dispatch.Invoke!(this, input, cancellationToken);
        await task.ConfigureAwait(false);

        if (dispatch.ExtractOutcome != null)
        {
            return dispatch.ExtractOutcome(task);
        }

        return Outcome.Success();
    }

    public void AttachChannel(string queueName, Channel<QueueMessage> channel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        ArgumentNullException.ThrowIfNull(channel);

        _activeChannels[queueName] = channel;
    }

        public void Dispose()
    {
        if (!_cancellationTokenSource.IsCancellationRequested)
        {
            _cancellationTokenSource.Cancel();
        }

        foreach (var channel in _activeChannels.Values)
        {
            channel.Writer.TryComplete();
        }

        _linkedTokenSource?.Dispose();
        _linkedTokenSource = null;
        _cancellationTokenSource.Dispose();

        SetStatus(WorkerStatus.Stopped);
    }
}
