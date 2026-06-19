using Atelier.Framework.Attributes;

namespace Atelier.Framework.Attache;

[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public class BoutiqueManifest
{
    public required string BoutiqueId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Version { get; set; }
    public List<ProductManifest> Products { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new();
    public BoutiqueCapabilities Capabilities { get; set; } = new();
}

public class ProductManifest
{
    public required string ProductTypeName { get; set; }
    public Type? ProductType { get; set; }
    public Dictionary<string, object> Configuration { get; set; } = new();
    public bool AutoStart { get; set; } = true;
}

public class BoutiqueCapabilities
{
    public bool SupportsRest { get; set; } = true;
    public bool SupportsGrpc { get; set; } = false;
    public bool SupportsWebSocket { get; set; } = false;
    public bool SupportsGraphQL { get; set; } = false;
    public bool SupportsUI { get; set; } = false;
    public List<string> SupportedProtocols { get; set; } = new();
}
