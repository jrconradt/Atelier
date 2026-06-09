using System.Text;
using System.Text.Json;
using Atelier.Framework.Context;
using Atelier.Framework.Attributes;
using Atelier.Framework.Network.Transport;
using Atelier.Framework.Properties;

namespace Atelier.Framework.Queueing.Core;

[ContractAttribute("QueueMessage", Version = "1.0", Namespace = "Framework.Queueing.Core")]
public class QueueMessage
{
        private static readonly JsonSerializerOptions PayloadSerializerOptions = new() { MaxDepth = 32 };

        public string Id { get; init; } = Guid.NewGuid().ToString();

        public string MessageType { get; }

        public string Payload { get; }

        public DateTime CreatedAt { get; } = DateTime.UtcNow;

        public DateTime? ScheduledFor { get; set; }

        public int RetryCount { get; set; } = 0;

        public int MaxRetries { get; set; } = 3;

        public int Priority { get; set; } = 0;

        public int? TimeToLiveSeconds { get; set; }

        public string? CorrelationId { get; set; }

        public string? TraceId { get; set; }

        public string? SpanId { get; set; }

        public string? ParentSpanId { get; set; }

        public IContext? Context { get; set; }

        public QueueMessageMetadata Metadata { get; set; } = new();

        public Dictionary<string, string> Headers { get; set; } = new();

        public QueueMessage(string messageType, string payload)
    {
        MessageType = messageType ?? throw new ArgumentNullException(nameof(messageType));
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
    }

        public QueueMessage(string messageType, object payload)
    {
        MessageType = messageType ?? throw new ArgumentNullException(nameof(messageType));
        Payload = JsonSerializer.Serialize(payload ?? throw new ArgumentNullException(nameof(payload)));
    }

        public T DeserializePayload<T>()
    {
        GuardPayloadSize();
        return JsonSerializer.Deserialize<T>(Payload, PayloadSerializerOptions) ?? throw new InvalidOperationException("Failed to deserialize payload");
    }

        private void GuardPayloadSize()
    {
        var payloadBytes = Encoding.UTF8.GetByteCount(Payload);
        if (payloadBytes > TransportMessage.MAX_PAYLOAD_SIZE)
        {
            throw new InvalidOperationException($"Payload size {payloadBytes} exceeds maximum {TransportMessage.MAX_PAYLOAD_SIZE} bytes");
        }
    }

        public QueueMessage WithUpdates(Action<QueueMessage> updates)
    {
        ArgumentNullException.ThrowIfNull(updates);

        var copy = new QueueMessage(MessageType, Payload)
        {
            Id = Id,
            ScheduledFor = ScheduledFor,
            RetryCount = RetryCount,
            MaxRetries = MaxRetries,
            Priority = Priority,
            TimeToLiveSeconds = TimeToLiveSeconds,
            CorrelationId = CorrelationId,
            TraceId = TraceId,
            SpanId = SpanId,
            ParentSpanId = ParentSpanId,
            Context = Context,
            Metadata = CopyMetadata(Metadata),
            Headers = new Dictionary<string, string>(Headers)
        };

        updates(copy);
        return copy;
    }

        private static QueueMessageMetadata CopyMetadata(QueueMessageMetadata source)
    {
        var copy = new QueueMessageMetadata();
        foreach (var kvp in source.GetAll())
        {
            copy[kvp.Key] = kvp.Value;
        }

        return copy;
    }

        public QueueMessage CreateRetry()
    {
        return WithUpdates(msg => msg.RetryCount++);
    }

        public T DeserializePayload<T>(JsonSerializerOptions? options = null)
    {
        GuardPayloadSize();
        return JsonSerializer.Deserialize<T>(Payload, options ?? PayloadSerializerOptions) ?? throw new InvalidOperationException("Failed to deserialize payload");
    }
}