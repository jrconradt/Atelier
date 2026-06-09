using Atelier.Framework.Primitives;
using Atelier.Framework.Infrastructure;
using Atelier.Framework.Attributes;
using Atelier.Framework.Observability;
using Prometheus;

namespace Atelier.Framework.EventStream.Observability;

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class EventStreamMetrics : IAtelier
{
    private static readonly Counter MessagesProcessed = Metrics.CreateCounter(
        "atelier_consumer_messages_processed_total",
        "Total messages successfully processed by consumers",
        new CounterConfiguration
        {
            LabelNames = new[] { "consumer", "topic" }
        });

    private static readonly Counter MessagesFailed = Metrics.CreateCounter(
        "atelier_consumer_messages_failed_total",
        "Total messages that failed processing",
        new CounterConfiguration
        {
            LabelNames = new[] { "consumer", "topic", "error_type" }
        });

    private static readonly Histogram ProcessingDuration = Metrics.CreateHistogram(
        "atelier_consumer_processing_duration_seconds",
        "Message processing duration in seconds",
        new HistogramConfiguration
        {
            LabelNames = new[] { "consumer", "topic" },
            Buckets = new[] { 0.001, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10 }
        });

    private static readonly Gauge AverageProcessingTime = Metrics.CreateGauge(
        "atelier_consumer_avg_processing_time_ms",
        "Average message processing time in milliseconds",
        new GaugeConfiguration
        {
            LabelNames = new[] { "consumer" }
        });

    private static readonly Gauge ConsumerGroupPending = Metrics.CreateGauge(
        "atelier_consumer_group_pending",
        "Number of pending messages in consumer group",
        new GaugeConfiguration
        {
            LabelNames = new[] { "group", "topic" }
        });

    private static readonly Counter StreamMessagesPublished = Metrics.CreateCounter(
        "atelier_stream_messages_published_total",
        "Total messages published to event streams",
        new CounterConfiguration
        {
            LabelNames = new[] { "stream", "producer" }
        });

    private static readonly Gauge StreamLength = Metrics.CreateGauge(
        "atelier_stream_length",
        "Current number of messages in stream",
        new GaugeConfiguration
        {
            LabelNames = new[] { "stream" }
        });

    private static readonly Counter QueueMessagesEnqueued = Metrics.CreateCounter(
        "atelier_queue_messages_enqueued_total",
        "Total messages enqueued to queues",
        new CounterConfiguration
        {
            LabelNames = new[] { "queue" }
        });

    private static readonly Gauge QueuePendingMessages = Metrics.CreateGauge(
        "atelier_queue_pending_messages",
        "Number of pending messages in queue",
        new GaugeConfiguration
        {
            LabelNames = new[] { "queue" }
        });

    private static readonly Gauge WorkerStatus = Metrics.CreateGauge(
        "atelier_worker_status",
        "Worker status (0=Stopped, 1=Starting, 2=Running, 3=Stopping, 4=Error)",
        new GaugeConfiguration
        {
            LabelNames = new[] { "worker" }
        });

    private static readonly Counter GrpcRequests = Metrics.CreateCounter(
        "atelier_grpc_requests_total",
        "Total gRPC requests received",
        new CounterConfiguration
        {
            LabelNames = new[] { "method", "status" }
        });

    [Operation("RecordMessageProcessed")]
    public void RecordMessageProcessed(string consumer, string topic)
    {
        MessagesProcessed.WithLabels(consumer, topic).Inc();
    }

    [Operation("RecordMessageFailed")]
    public void RecordMessageFailed(string consumer, string topic, string errorType)
    {
        MessagesFailed.WithLabels(consumer, topic, errorType).Inc();
    }

    [Operation("MeasureProcessingDuration")]
    public IDisposable MeasureProcessingDuration(string consumer, string topic)
    {
        return ProcessingDuration.WithLabels(consumer, topic).NewTimer();
    }

    [Operation("RecordProcessingTime")]
    public void RecordProcessingTime(string consumer, double milliseconds)
    {
        AverageProcessingTime.WithLabels(consumer).Set(milliseconds);
    }

    [Operation("SetConsumerGroupPending")]
    public void SetConsumerGroupPending(string group, string topic, long pending)
    {
        ConsumerGroupPending.WithLabels(group, topic).Set(pending);
    }

    [Operation("RecordStreamMessagePublished")]
    public void RecordStreamMessagePublished(string stream, string producer)
    {
        StreamMessagesPublished.WithLabels(stream, producer).Inc();
    }

    [Operation("SetStreamLength")]
    public void SetStreamLength(string stream, long length)
    {
        StreamLength.WithLabels(stream).Set(length);
    }

    [Operation("RecordQueueMessageEnqueued")]
    public void RecordQueueMessageEnqueued(string queue)
    {
        QueueMessagesEnqueued.WithLabels(queue).Inc();
    }

    [Operation("SetQueuePending")]
    public void SetQueuePending(string queue, long pending)
    {
        QueuePendingMessages.WithLabels(queue).Set(pending);
    }

    [Operation("SetWorkerStatus")]
    public void SetWorkerStatus(string worker, WorkerState state)
    {
        WorkerStatus.WithLabels(worker).Set((int)state);
    }

    [Operation("RecordGrpcRequest")]
    public void RecordGrpcRequest(string method, string status)
    {
        GrpcRequests.WithLabels(method, status).Inc();
    }
}

public enum WorkerState
{
    Stopped = 0,
    Starting = 1,
    Running = 2,
    Stopping = 3,
    Error = 4
}
