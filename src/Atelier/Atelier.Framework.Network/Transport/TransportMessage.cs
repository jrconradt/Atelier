using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Atelier.Framework.Context;
using Atelier.Framework.Attributes;
namespace Atelier.Framework.Network.Transport
{
    [ContractAttribute(
"TransportMessage",
Version = "1.0",
Namespace = "Framework.Network.Transport")]
    public class TransportMessage : ITransportMessage
    {
        public const int MAX_PAYLOAD_SIZE = 10 * 1024 * 1024;
        public const string RESPONSE_ERROR_CODE_HEADER = "Atelier-Response-ErrorCode";
        public const string RESPONSE_ERROR_MESSAGE_HEADER = "Atelier-Response-ErrorMessage";
        private const int WIRE_MAX_DEPTH = 32;
        private static readonly JsonSerializerOptions SharedSerializerOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = false, MaxDepth = WIRE_MAX_DEPTH };
        private Dictionary<string, string>? _headers;
        public string MessageId
        {
            get; set;
        } = Guid.NewGuid().ToString();
        public string MessageType
        {
            get; set;
        } = string.Empty;
        public byte[] Payload
        {
            get; set;
        } = Array.Empty<byte>();
        public DateTime Timestamp
        {
            get; set;
        } = DateTime.UtcNow;
        public Dictionary<string, string> Headers
        {
            get => _headers ??= new Dictionary<string, string>(); set => _headers = value;
        }
        public string Source
        {
            get; set;
        } = string.Empty;
        public string Destination
        {
            get; set;
        } = string.Empty;
        [JsonIgnore]
        public AuthorizationContext? VerifiedAuthorization { get; set; }
        public bool HasHeaders => _headers != null && _headers.Count > 0;
        public T DeserializePayload<T>()
        {
            if (Payload == null || Payload.Length == 0)
            {
                throw new InvalidOperationException("Cannot deserialize empty payload");
            }
            if (Payload.Length > MAX_PAYLOAD_SIZE)
            {
                throw new InvalidOperationException($"Payload size {Payload.Length} exceeds maximum {MAX_PAYLOAD_SIZE} bytes");
            }
            return JsonSerializer.Deserialize<T>(Payload, SharedSerializerOptions)
                ?? throw new InvalidOperationException("Failed to deserialize payload");
        }
        public static TransportMessage Create<T>(string messageType, T payload, string destination = "")
        {
            var message = new TransportMessage
            {
                MessageType = messageType,
                Destination = destination,
                Timestamp = DateTime.UtcNow
            };

            if (payload != null)
            {
                var serialized = JsonSerializer.SerializeToUtf8Bytes(payload, SharedSerializerOptions);
                if (serialized.Length > MAX_PAYLOAD_SIZE)
                {
                    throw new InvalidOperationException($"Payload size {serialized.Length} exceeds maximum {MAX_PAYLOAD_SIZE} bytes");
                }

                message.Payload = serialized;
            }

            return message;
        }
        public void SetHeader(string key, string value)
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(value);
            Headers[key] = value;
        }
        public bool TryGetHeader(string key, out string? value)
        {
            ArgumentNullException.ThrowIfNull(key);
            if (_headers == null)
            {
                value = null;
                return false;
            }
            return _headers.TryGetValue(key, out value);
        }
    }
}
