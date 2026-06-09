
namespace Atelier.Framework.Queueing.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class QueueWorkerAttribute : Attribute
{
        public string QueueName { get; }

        public string[] MessageTypes { get; }

        public int MaxConcurrency { get; set; } = 1;

        public int BatchSize { get; set; } = 1;

        public bool EnableDeadLetterQueue { get; set; } = true;

        public int MaxRetries { get; set; } = 3;

        public int RetryDelayMs { get; set; } = 1000;

        public int Priority { get; set; } = 0;

        public QueueWorkerAttribute(string queueName)
    {
        QueueName = queueName ?? throw new ArgumentNullException(nameof(queueName));
        MessageTypes = Array.Empty<string>();
    }

        public QueueWorkerAttribute(string queueName, params string[] messageTypes)
    {
        QueueName = queueName ?? throw new ArgumentNullException(nameof(queueName));
        MessageTypes = messageTypes ?? Array.Empty<string>();
    }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class MessageHandlerAttribute : Attribute
{
        public string MessageType { get; }

        public bool HandleAllTypes { get; }

        public ExecutionStrategy Strategy { get; set; } = ExecutionStrategy.Sequential;

        public bool StopOnFailure { get; set; } = true;

        public int TimeoutMs { get; set; } = 30000;

        public MessageHandlerAttribute(string? messageType = null)
    {
        MessageType = messageType ?? string.Empty;
    }

        public MessageHandlerAttribute()
    {
        MessageType = string.Empty;
        HandleAllTypes = true;
    }
}

public enum ExecutionStrategy
{
        Sequential,

        Parallel,

        Batch
}

[AttributeUsage(AttributeTargets.Class)]
public class QueueWorkerLifecycleAttribute : Attribute
{
        public WorkerLifecycle Lifecycle { get; set; } = WorkerLifecycle.Singleton;

        public int MaxPoolSize { get; set; } = 10;

        public int InitialPoolSize { get; set; } = 1;

        public bool AutoStart { get; set; } = true;

        public int HealthCheckIntervalMs { get; set; } = 30000;
}

public enum WorkerLifecycle
{
        Singleton,

        Scoped,

        Transient,

        Pooled
}
