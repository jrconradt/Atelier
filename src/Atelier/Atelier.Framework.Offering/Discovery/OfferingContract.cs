namespace Atelier.Framework.Offering.Discovery;

public class OfferingContract
{
    public string ContractName { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0";
    public string[] RequiredScopes { get; set; } = Array.Empty<string>();
    public string[] RequiredClaims { get; set; } = Array.Empty<string>();
    public Dictionary<string, string> Metadata { get; set; } = new();
}
