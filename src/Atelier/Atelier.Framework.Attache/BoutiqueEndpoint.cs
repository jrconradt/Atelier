using Atelier.Framework.Attributes;

namespace Atelier.Framework.Attache;

public class BoutiqueEndpoint
{
    public required EndpointType Type { get; set; }
    public required string BaseUrl { get; set; }
    public string? BasePath { get; set; }
    public EndpointStatus Status { get; set; } = EndpointStatus.Active;
    public Dictionary<string, string> Metadata { get; set; } = new();
}

public enum EndpointType
{
    Rest,
    Grpc,
    WebSocket,
    GraphQL,
    Custom
}

public enum EndpointStatus
{
    Active,
    Inactive,
    Degraded,
    Unknown
}

[Contract("BoutiqueUiMetadata", Version = "1.0", Namespace = "Framework.Attache")]
public class BoutiqueUiMetadata
{
    public string? UiUrl { get; set; }
    public string? IconUrl { get; set; }
    public string? Theme { get; set; }
    public Dictionary<string, string> Properties { get; set; } = new();
}
