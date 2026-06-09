using Atelier.Framework.Attributes;

namespace Atelier.Framework.Facility;

[Contract("CapabilityQuery", Version = "1.0")]
public class CapabilityQuery
{
    public HashSet<string> RequiredOperations { get; set; } = new();
    public HashSet<string> RequiredScopes { get; set; } = new();
    public HashSet<string> RequiredClaims { get; set; } = new();
    public HashSet<Type> RequiredTypes { get; set; } = new();
    public Dictionary<string, string> MetadataFilters { get; set; } = new();
    public bool RequireAll { get; set; } = true;
}
