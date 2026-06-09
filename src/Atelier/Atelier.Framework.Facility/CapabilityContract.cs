using Atelier.Framework.Attributes;

namespace Atelier.Framework.Facility;

[Contract("CapabilityContract", Version = "1.0")]
public class CapabilityContract
{
    public string OperationId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string RequestTypeName { get; set; } = string.Empty;
    public string ResponseTypeName { get; set; } = string.Empty;
    public List<string> RequiredScopes { get; set; } = new();
    public List<string> RequiredClaims { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new();
}
