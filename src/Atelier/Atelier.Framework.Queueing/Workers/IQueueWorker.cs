using System.Threading.Channels;
using Atelier.Framework.Context;
using Atelier.Framework.Attributes;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Queueing.Attributes;

namespace Atelier.Framework.Queueing.Workers;

public interface IQueueWorker
{
        public string WorkerName { get; }

        public IEnumerable<QueueWorkerConfiguration> QueueConfigurations { get; }

        public IEnumerable<MessageHandlerConfiguration> MessageHandlerConfigurations { get; }

        public Task<Outcome> StartAsync(CancellationToken cancellationToken = default);

        public Task<Outcome> StopAsync(CancellationToken cancellationToken = default);

        public WorkerStatus Status { get; }

        public WorkerStats GetStats();
}

[Contract("QueueWorkerConfiguration", Version = "1.0", Namespace = "Framework.Queueing.Workers")]
public class QueueWorkerConfiguration
{
        public string QueueName { get; } = null!;

        public string[] MessageTypes { get; } = null!;

        public int MaxConcurrency { get; }

        public int BatchSize { get; }

        public bool EnableDeadLetterQueue { get; }

        public string? DeadLetterQueueName { get; }

        public int MaxRetries { get; }

        public int RetryDelayMs { get; }

        public int Priority { get; }

        public QueueWorkerConfiguration(
        string queueName,
        string[] messageTypes,
        int maxConcurrency = 1,
        int batchSize = 1,
        bool enableDeadLetterQueue = true,
        int maxRetries = 3,
        int retryDelayMs = 1000,
        int priority = 0,
        string? deadLetterQueueName = null)
    {
        QueueName = queueName;
        MessageTypes = messageTypes;
        MaxConcurrency = maxConcurrency;
        BatchSize = batchSize;
        EnableDeadLetterQueue = enableDeadLetterQueue;
        MaxRetries = maxRetries;
        RetryDelayMs = retryDelayMs;
        Priority = priority;
        DeadLetterQueueName = deadLetterQueueName;
    }
}

[Contract("MessageHandlerConfiguration", Version = "1.0", Namespace = "Framework.Queueing.Workers")]
public class MessageHandlerConfiguration
{
        public string MessageType { get; }

        public bool HandleAllTypes { get; }

        public ExecutionStrategy Strategy { get; }

        public bool StopOnFailure { get; }

        public int TimeoutMs { get; }

        public MessageHandlerConfiguration(
        string messageType,
        bool handleAllTypes = false,
        ExecutionStrategy strategy = ExecutionStrategy.Sequential,
        bool stopOnFailure = true,
        int timeoutMs = 30000)
    {
        MessageType = messageType;
        HandleAllTypes = handleAllTypes;
        Strategy = strategy;
        StopOnFailure = stopOnFailure;
        TimeoutMs = timeoutMs;
    }
}

public enum WorkerStatus
{
        Stopped,

        Starting,

        Running,

        Stopping,

        Error,

        Paused
}

public class WorkerStats
{
        public WorkerStatus Status { get; set; }

        public long MessagesProcessed { get; set; }

        public long MessagesFailed { get; set; }

        public long MessagesInProgress { get; set; }

        public double AverageProcessingTimeMs { get; set; }

        public DateTime LastProcessedAt { get; set; }

        public DateTime StartedAt { get; set; }

        public TimeSpan Uptime => DateTime.UtcNow - StartedAt;

        public string? LastError { get; set; }
}
