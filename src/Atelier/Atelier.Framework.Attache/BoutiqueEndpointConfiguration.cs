using Atelier.Framework.Attributes;

namespace Atelier.Framework.Attache;

[Contract("BoutiqueEndpointConfiguration", Version = "1.0", Namespace = "Framework.Attache")]
public class BoutiqueEndpointConfiguration
{
    public string InProcessBaseUrl { get; set; } = "http://localhost:5000";
    public string OutOfProcessBaseUrl { get; set; } = "http://localhost:8080";
    public string NetworkMappedBaseUrl { get; set; } = "https://boutique.example.com";
    public string GrpcBaseUrl { get; set; } = "grpc://localhost:9090";
    public string WebSocketBaseUrl { get; set; } = "ws://localhost:8081";
}
