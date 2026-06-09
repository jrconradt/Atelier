using Prometheus;

namespace Atelier.Framework.Observability;

public static class ApplicationMetrics
{
    public static readonly Counter QueueMessagesEnqueuedTotal = Prometheus.Metrics.CreateCounter(
        "atelier_application_queue_messages_enqueued_total",
        "Total number of messages enqueued to queues",
        new CounterConfiguration
        {
            LabelNames = new[] { "queue", "instance", "mode" }
        });

    public static readonly Counter QueueMessagesDequeuedTotal = Prometheus.Metrics.CreateCounter(
        "atelier_application_queue_messages_dequeued_total",
        "Total number of messages dequeued from queues",
        new CounterConfiguration
        {
            LabelNames = new[] { "queue", "instance", "mode" }
        });

    public static readonly Counter QueueMessagesProcessedTotal = Prometheus.Metrics.CreateCounter(
        "atelier_application_queue_messages_processed_total",
        "Total number of messages successfully processed from queues",
        new CounterConfiguration
        {
            LabelNames = new[] { "queue", "instance", "mode" }
        });

    public static readonly Counter QueueMessagesFailedTotal = Prometheus.Metrics.CreateCounter(
        "atelier_application_queue_messages_failed_total",
        "Total number of messages that failed processing",
        new CounterConfiguration
        {
            LabelNames = new[] { "queue", "instance", "mode" }
        });

    public static readonly Histogram QueueProcessingDuration = Prometheus.Metrics.CreateHistogram(
        "atelier_application_queue_processing_duration_seconds",
        "Duration of queue message processing in seconds",
        new HistogramConfiguration
        {
            LabelNames = new[] { "queue", "instance", "mode" },
            Buckets = Histogram.ExponentialBuckets(0.01, 2, 10)
        });

    public static readonly Counter MessagingDispatchTotal = Prometheus.Metrics.CreateCounter(
        "atelier_application_messaging_dispatch_total",
        "Total number of in-process message dispatches",
        new CounterConfiguration
        {
            LabelNames = new[] { "request_type", "result", "instance", "mode" }
        });

    public static readonly Counter MessagingDispatchErrorsTotal = Prometheus.Metrics.CreateCounter(
        "atelier_application_messaging_dispatch_errors_total",
        "Total number of in-process message dispatches that failed",
        new CounterConfiguration
        {
            LabelNames = new[] { "request_type", "error_code", "instance", "mode" }
        });

    public static readonly Histogram MessagingDispatchDuration = Prometheus.Metrics.CreateHistogram(
        "atelier_application_messaging_dispatch_duration_seconds",
        "Duration of in-process message dispatch in seconds",
        new HistogramConfiguration
        {
            LabelNames = new[] { "request_type", "result", "instance", "mode" },
            Buckets = Histogram.ExponentialBuckets(0.001, 2, 12)
        });

    private static string? _instanceId;
    private static string? _boutiqueMode;

    private static readonly string ProcessInstanceId =
        $"{Environment.MachineName}-{Environment.ProcessId}";

    public static void Initialize(string instanceId, string boutiqueMode)
    {
        _instanceId = instanceId;
        _boutiqueMode = boutiqueMode;
    }

    public static string InstanceId =>
        _instanceId
        ?? Environment.GetEnvironmentVariable("ATELIER_INSTANCE_ID")
        ?? ProcessInstanceId;

    public static string BoutiqueMode =>
        _boutiqueMode
        ?? Environment.GetEnvironmentVariable("ATELIER_BOUTIQUE_MODE")
        ?? "InProcess";
}
