using Atelier.Framework.Attributes;
using System.Globalization;
using System.Reflection;

namespace Atelier.Framework.Network.Transport
{
    [ContractAttribute("TransportMessageDto", Version = "1.0", Namespace = "Framework.Network.Transport")]
    public class TransportMessageDto
    {
        public static readonly int CURRENT_SCHEMA_VERSION = ResolveSchemaVersion();

        private static int ResolveSchemaVersion()
        {
            var contract = typeof(TransportMessageDto).GetCustomAttribute<ContractAttribute>();
            var version = contract?.Version ?? "1.0";
            var major = version.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "1";
            return int.TryParse(major, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 1;
        }

        public int SchemaVersion { get; set; } = CURRENT_SCHEMA_VERSION;
        public string MessageId { get; set; } = string.Empty;
        public string MessageType { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public Dictionary<string, string> Headers { get; set; } = new();
        public string Source { get; set; } = string.Empty;
        public string Destination { get; set; } = string.Empty;

        public static TransportMessageDto FromTransportMessage(TransportMessage message)
        {
            return new TransportMessageDto
            {
                SchemaVersion = CURRENT_SCHEMA_VERSION,
                MessageId = message.MessageId,
                MessageType = message.MessageType,
                Payload = message.Payload.Length > 0 ? Convert.ToBase64String(message.Payload) : string.Empty,
                Timestamp = message.Timestamp,
                Headers = message.Headers,
                Source = message.Source,
                Destination = message.Destination
            };
        }

        public TransportMessage ToTransportMessage()
        {
            if (SchemaVersion > CURRENT_SCHEMA_VERSION)
            {
                throw new InvalidOperationException($"Transport message schema version {SchemaVersion} exceeds supported version {CURRENT_SCHEMA_VERSION}");
            }

            byte[] payloadBytes;
            if (string.IsNullOrEmpty(Payload))
            {
                payloadBytes = Array.Empty<byte>();
            }
            else
            {
                var maxBase64Length = (TransportMessage.MAX_PAYLOAD_SIZE / 3L * 4L) + 4L;
                if (Payload.Length > maxBase64Length)
                {
                    throw new InvalidOperationException($"Encoded payload length {Payload.Length} exceeds maximum {maxBase64Length} characters");
                }

                var estimatedBytes = ((long)Payload.Length * 3) / 4;
                if (estimatedBytes > TransportMessage.MAX_PAYLOAD_SIZE)
                {
                    throw new InvalidOperationException($"Payload size {estimatedBytes} exceeds maximum {TransportMessage.MAX_PAYLOAD_SIZE} bytes");
                }

                var buffer = new byte[estimatedBytes + 3];
                if (!Convert.TryFromBase64String(Payload, buffer, out var written))
                {
                    throw new InvalidOperationException("Payload is not a valid Base64 string");
                }

                if (written > TransportMessage.MAX_PAYLOAD_SIZE)
                {
                    throw new InvalidOperationException($"Payload size {written} exceeds maximum {TransportMessage.MAX_PAYLOAD_SIZE} bytes");
                }

                payloadBytes = buffer.AsSpan(0, written).ToArray();
            }

            return new TransportMessage
            {
                MessageId = MessageId,
                MessageType = MessageType,
                Payload = payloadBytes,
                Timestamp = Timestamp,
                Headers = Headers,
                Source = Source,
                Destination = Destination
            };
        }
    }
}
