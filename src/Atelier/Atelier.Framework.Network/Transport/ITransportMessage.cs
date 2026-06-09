using System.Text.Json;
using Atelier.Framework.Context;

namespace Atelier.Framework.Network.Transport
{
    public static class TransportJson
    {
        public const int MAX_DEPTH = 32;

        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            MaxDepth = MAX_DEPTH,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
    }

    public interface ITransportMessage
    {
        public string MessageId { get; set; }
        public string MessageType { get; set; }
        public byte[] Payload { get; set; }
        public DateTime Timestamp { get; set; }
        public Dictionary<string, string> Headers { get; set; }
        public string Source { get; set; }
        public string Destination { get; set; }
        public AuthorizationContext? VerifiedAuthorization { get; set; }
    }
}
