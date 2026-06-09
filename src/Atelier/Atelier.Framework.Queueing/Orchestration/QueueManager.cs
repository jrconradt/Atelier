using Atelier.Framework.Primitives;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Atelier.Framework.Infrastructure;
using Atelier.Framework.Attributes;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Queueing.Core;
using Atelier.Framework.Queueing.Workers;
using Atelier.Framework.Requisitions;
namespace Atelier.Framework.Queueing.Orchestration;

[Infrastructure(InfrastructureLifetime.Singleton)]
public partial class QueueManager : IAtelier, IQueueManager, IDisposable
{
    private readonly ConcurrentDictionary<string, IQueue> _queues = new();
    private readonly ConcurrentDictionary<string, IQueueWorker> _workers = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly StrongBox<int> _disposed = new(0);

    private readonly ConcurrentDictionary<string, QueueTelemetry> _queueTelemetry = new();

        private sealed class QueueTelemetry
    {
        private long _completedCount;
        private long _failedCount;
        private long _totalProcessingTimeMs;
        private long _inFlightCount;

        public void IncrementInFlight()
        {
            Interlocked.Increment(ref _inFlightCount);
        }

        public void Record(bool success, long processingTimeMs)
        {
            if (success)
            {
                Interlocked.Increment(ref _completedCount);
            }
            else
            {
                Interlocked.Increment(ref _failedCount);
            }

            Interlocked.Add(ref _totalProcessingTimeMs, processingTimeMs);

            if (Interlocked.Decrement(ref _inFlightCount) < 0)
            {
                Interlocked.Exchange(ref _inFlightCount, 0);
            }
        }

        public (long completed, long failed, double avgProcessingMs, long inFlight) Snapshot()
        {
            var completed = Interlocked.Read(ref _completedCount);
            var failed = Interlocked.Read(ref _failedCount);
            var totalMs = Interlocked.Read(ref _totalProcessingTimeMs);
            var inFlight = Math.Max(0, Interlocked.Read(ref _inFlightCount));
            var total = completed + failed;
            var avgMs = total > 0
                ? totalMs / (double)total
                : 0;

            return (completed, failed, avgMs, inFlight);
        }
    }

        public QueueManager()
    {
    }

        public void RecordProcessingResult(string queueName,
                                           bool success,
                                           double processingTimeMs)
    {
        ArgumentNullException.ThrowIfNull(queueName);
        if (string.IsNullOrWhiteSpace(queueName))
        {
            return;
        }

        var telemetry = _queueTelemetry.GetOrAdd(queueName, _ => new QueueTelemetry());
        telemetry.Record(success, (long)processingTimeMs);
    }

        public void RecordMessageRead(string queueName)
    {
        ArgumentNullException.ThrowIfNull(queueName);
        if (string.IsNullOrWhiteSpace(queueName))
        {
            return;
        }

        var telemetry = _queueTelemetry.GetOrAdd(queueName, _ => new QueueTelemetry());
        telemetry.IncrementInFlight();
    }

        internal (long completed, long failed, double avgProcessingMs, long inFlight) GetQueueTelemetry(string queueName)
    {
        ArgumentNullException.ThrowIfNull(queueName);
        if (string.IsNullOrWhiteSpace(queueName))
        {
            return (0, 0, 0, 0);
        }

        if (!_queueTelemetry.TryGetValue(queueName, out var telemetry))
        {
            return (0, 0, 0, 0);
        }

        return telemetry.Snapshot();
    }

        public async Task<Outcome> RegisterWorkerAsync(IQueueWorker worker, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(worker);

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "Worker", worker.WorkerName);

        if (!_workers.TryAdd(worker.WorkerName, worker))
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", "Worker already registered"), ("WorkerName", worker.WorkerName)]);
            return Outcome.Failure();
        }

        foreach (var queueConfig in worker.QueueConfigurations)
        {
            var queueResult = await GetQueueAsync(queueConfig.QueueName, cancellationToken).ConfigureAwait(false);
            if (!queueResult.IsSuccess)
            {
                Observe(
                    LogLevel.Warning,
                    values: [("Reason", "Failed to create queue for worker"), ("WorkerName", worker.WorkerName), ("QueueName", queueConfig.QueueName)]);
                return Outcome.Failure();
            }

            var queue = queueResult.Data!;

            if (worker is QueueWorkerBase workerBase)
            {
                workerBase.AttachChannel(queueConfig.QueueName, queue.Channel);
            }
        }

        Observe(LogLevel.Information, values: [("WorkerName", worker.WorkerName), ("QueueCount", worker.QueueConfigurations.Count())]);

        return Outcome.Success();
    }

        public async Task<Outcome> UnregisterWorkerAsync(string workerName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workerName);

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "Worker", workerName);

        if (!_workers.TryRemove(workerName, out var worker))
        {
            Observe(
                LogLevel.Information,
                values: [("Message", "Unregister of absent worker treated as success"), ("WorkerName", workerName)]);
            return Outcome.Success();
        }

        if (worker.Status == WorkerStatus.Running || worker.Status == WorkerStatus.Starting)
        {
            await worker.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        Observe(LogLevel.Information, values: [("WorkerName", workerName)]);

        return Outcome.Success();
    }

        public async Task<Outcome<IQueue>> GetQueueAsync(string queueName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(queueName))
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", "Queue name was null or empty")]);
            return Outcome<IQueue>.Failure();
        }

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "Queue", queueName);

        if (_queues.TryGetValue(queueName, out var existingQueue))
        {
            return Outcome<IQueue>.Success(existingQueue);
        }

        var createResult = await CreateQueueAsync(queueName, null, cancellationToken).ConfigureAwait(false);
        return createResult;
    }

        public Task<Outcome<IQueue>> CreateQueueAsync(
        string queueName,
        QueueConfiguration? configuration = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(queueName))
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", "Queue name was null or empty")]);
            return Task.FromResult(Outcome<IQueue>.Failure());
        }

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "Queue", queueName);

        var channel = Channel.CreateBounded<QueueMessage>(
            configuration?.MaxCapacity ?? 1000);

        var queue = new InMemoryQueue(queueName, channel, configuration, this);
        var stored = _queues.GetOrAdd(queueName, queue);

        if (!ReferenceEquals(stored, queue))
        {
            channel.Writer.Complete();
            Observe(
                LogLevel.Warning,
                values: [("Reason", "Queue already exists"), ("QueueName", queueName)]);
            return Task.FromResult(Outcome<IQueue>.Failure());
        }

        Observe(LogLevel.Information, values: [("QueueName", queueName), ("MaxCapacity", configuration?.MaxCapacity ?? 1000)]);

        return Task.FromResult(Outcome<IQueue>.Success(queue));
    }

        public Task<Outcome> DeleteQueueAsync(string queueName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queueName);

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "Queue", queueName);

        if (!_queues.TryRemove(queueName, out var queue))
        {
            Observe(
                LogLevel.Information,
                values: [("Message", "Delete of absent queue treated as success"), ("QueueName", queueName)]);
            return Task.FromResult(Outcome.Success());
        }

        if (queue.Channel is Channel<QueueMessage> channel)
        {
            channel.Writer.Complete();
        }

        _queueTelemetry.TryRemove(queueName, out _);

        Observe(LogLevel.Information, values: [("QueueName", queueName)]);

        return Task.FromResult(Outcome.Success());
    }

        public Task<IEnumerable<IQueueWorker>> GetWorkersAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_workers.Values.AsEnumerable());
    }

        public Task<IEnumerable<IQueue>> GetQueuesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_queues.Values.AsEnumerable());
    }

        public async Task<QueueingStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var queueStats = new Dictionary<string, Core.QueueStats>();
        var workerStats = new Dictionary<string, Workers.WorkerStats>();

        var totalProcessed = 0L;
        var totalFailed = 0L;

        foreach (var queue in _queues.Values)
        {
            var stats = await queue.GetStatsAsync(cancellationToken).ConfigureAwait(false);
            queueStats[queue.Name] = stats;
        }

        foreach (var worker in _workers.Values)
        {
            var stats = worker.GetStats();
            workerStats[worker.WorkerName] = stats;
            totalProcessed += stats.MessagesProcessed;
            totalFailed += stats.MessagesFailed;
        }

        return new QueueingStats
        {
            QueueStats = queueStats,
            WorkerStats = workerStats,
            TotalMessagesProcessed = totalProcessed,
            TotalMessagesFailed = totalFailed,
            ActiveWorkers = _workers.Values.Count(w => w.Status == WorkerStatus.Running),
            CollectedAt = DateTime.UtcNow
        };
    }

        public async Task<Outcome> StartAllWorkersAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<Outcome>();

        foreach (var worker in _workers.Values)
        {
            var result = await worker.StartAsync(cancellationToken).ConfigureAwait(false);
            results.Add(result);
        }

        var failures = results.Where(r => !r.IsSuccess).ToList();
        if (failures.Any())
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", "Some workers failed to start"), ("FailureCount", failures.Count), ("WorkerCount", results.Count)]);
            return Outcome.Failure();
        }

        return Outcome.Success();
    }

        public async Task<Outcome> StopAllWorkersAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<Outcome>();

        foreach (var worker in _workers.Values)
        {
            var result = await worker.StopAsync(cancellationToken).ConfigureAwait(false);
            results.Add(result);
        }

        var failures = results.Where(r => !r.IsSuccess).ToList();
        if (failures.Any())
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", "Some workers failed to stop"), ("FailureCount", failures.Count), ("WorkerCount", results.Count)]);
            return Outcome.Failure();
        }

        return Outcome.Success();
    }

        public async Task<HealthCheckResult> PerformHealthCheckAsync(CancellationToken cancellationToken = default)
    {
        var result = new HealthCheckResult();
        var now = DateTime.UtcNow;

        foreach (var queue in _queues.Values)
        {
            var stats = await queue.GetStatsAsync(cancellationToken).ConfigureAwait(false);
            var queueHealth = new QueueHealthStatus
            {
                IsHealthy = true,
                LastActivity = stats.LastActivity ?? DateTime.MinValue
            };

            if (stats.LastActivity.HasValue && (now - stats.LastActivity.Value).TotalMinutes > 5)
            {
                queueHealth.IsHealthy = false;
                result.Warnings.Add($"Queue '{queue.Name}' has been inactive for more than 5 minutes");
            }

            result.QueueHealth[queue.Name] = queueHealth;
        }

        foreach (var worker in _workers.Values)
        {
            var stats = worker.GetStats();
            var workerHealth = new WorkerHealthStatus
            {
                IsHealthy = stats.Status != WorkerStatus.Error,
                Status = stats.Status,
                LastActivity = stats.LastProcessedAt
            };

            if (stats.Status == WorkerStatus.Error)
            {
                workerHealth.Error = stats.LastError;
                result.Errors.Add($"Worker '{worker.WorkerName}' is in error state: {stats.LastError}");
            }
            else if ((now - stats.LastProcessedAt).TotalMinutes > 10)
            {
                result.Warnings.Add($"Worker '{worker.WorkerName}' has been inactive for more than 10 minutes");
            }

            result.WorkerHealth[worker.WorkerName] = workerHealth;
        }

        result.IsHealthy = !result.Errors.Any() && result.QueueHealth.All(q => q.Value.IsHealthy) && result.WorkerHealth.All(w => w.Value.IsHealthy);

        return result;
    }

        public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed.Value, 1) != 0)
        {
            return;
        }

        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();

        foreach (var worker in _workers.Values)
        {
            if (worker is IDisposable disposableWorker)
            {
                disposableWorker.Dispose();
            }
        }

        foreach (var queue in _queues.Values)
        {
            if (queue.Channel is Channel<QueueMessage> channel)
            {
                channel.Writer.Complete();
            }
        }

        _workers.Clear();
        _queues.Clear();
        _queueTelemetry.Clear();
    }
}

