using Atelier.Framework.Attributes;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Queueing.Core;
using Atelier.Framework.Queueing.Workers;

namespace Atelier.Framework.Queueing.Orchestration;

public interface IQueueManager
{
        public Task<Outcome> RegisterWorkerAsync(IQueueWorker worker, CancellationToken cancellationToken = default);

        public Task<Outcome> UnregisterWorkerAsync(string workerName, CancellationToken cancellationToken = default);

        public Task<Outcome<IQueue>> GetQueueAsync(string queueName, CancellationToken cancellationToken = default);

        public Task<Outcome<IQueue>> CreateQueueAsync(
        string queueName,
        QueueConfiguration? configuration = null,
        CancellationToken cancellationToken = default);

        public Task<Outcome> DeleteQueueAsync(string queueName, CancellationToken cancellationToken = default);

        public Task<IEnumerable<IQueueWorker>> GetWorkersAsync(CancellationToken cancellationToken = default);

        public Task<IEnumerable<IQueue>> GetQueuesAsync(CancellationToken cancellationToken = default);

        public Task<QueueingStats> GetStatsAsync(CancellationToken cancellationToken = default);

        public Task<Outcome> StartAllWorkersAsync(CancellationToken cancellationToken = default);

        public Task<Outcome> StopAllWorkersAsync(CancellationToken cancellationToken = default);

        public Task<HealthCheckResult> PerformHealthCheckAsync(CancellationToken cancellationToken = default);

        void RecordProcessingResult(string queueName,
                                    bool success,
                                    double processingTimeMs);

        void RecordMessageRead(string queueName);
}

[Contract("QueueConfiguration", Version = "1.0", Namespace = "Framework.Queueing.Orchestration")]
public class QueueConfiguration
{
        public int MaxCapacity { get; set; } = 1000;

        public bool EnablePersistence { get; set; } = false;

        public string? PersistenceConnectionString { get; set; }

        public string? DeadLetterQueueName { get; set; }

        public long MessageTimeToLiveMs { get; set; } = 86400000;

        public int ProcessingTimeoutMs { get; set; } = 30000;

        public RetryPolicy RetryPolicy { get; set; } = new();

        public Dictionary<string, string> Metadata { get; set; } = new();
}

public class RetryPolicy
{
        public int MaxRetries { get; set; } = 3;

        public int RetryDelayMs { get; set; } = 1000;

        public double BackoffMultiplier { get; set; } = 2.0;

        public int MaxRetryDelayMs { get; set; } = 300000;
}

public class QueueingStats
{
        public Dictionary<string, Core.QueueStats> QueueStats { get; set; } = new();

        public Dictionary<string, Workers.WorkerStats> WorkerStats { get; set; } = new();

        public long TotalMessagesProcessed { get; set; }

        public long TotalMessagesFailed { get; set; }

        public int ActiveWorkers { get; set; }

        public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

[Contract("HealthCheckResult", Version = "1.0", Namespace = "Framework.Queueing.Orchestration")]
public class HealthCheckResult
{
        public bool IsHealthy { get; set; }

        public Dictionary<string, QueueHealthStatus> QueueHealth { get; set; } = new();

        public Dictionary<string, WorkerHealthStatus> WorkerHealth { get; set; } = new();

        public List<string> Errors { get; set; } = new();

        public List<string> Warnings { get; set; } = new();
}

public class QueueHealthStatus
{
        public bool IsHealthy { get; set; }

        public DateTime LastActivity { get; set; }

        public string? Error { get; set; }
}

public class WorkerHealthStatus
{
        public bool IsHealthy { get; set; }

        public Workers.WorkerStatus Status { get; set; }

        public DateTime LastActivity { get; set; }

        public string? Error { get; set; }
}
