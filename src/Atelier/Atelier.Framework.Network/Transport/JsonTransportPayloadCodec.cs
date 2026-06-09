using System.Text.Json;

namespace Atelier.Framework.Network.Transport
{
    public sealed class JsonTransportPayloadCodec : ITransportPayloadCodec
    {
        public const string CONTENT_TYPE = "application/json";

        public static readonly JsonTransportPayloadCodec Instance = new JsonTransportPayloadCodec();

        public string ContentType => CONTENT_TYPE;

        public byte[] Serialize<T>(T value)
        {
            return JsonSerializer.SerializeToUtf8Bytes(value, TransportJson.Options);
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> payload)
        {
            return JsonSerializer.Deserialize<T>(payload.Span, TransportJson.Options)
                ?? throw new InvalidOperationException("Failed to deserialize payload");
        }
    }
}
