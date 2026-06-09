namespace Atelier.Framework.Facility;

public class SecurityRequirements
{
    public bool RequiresAuthentication { get; set; } = true;
    public HashSet<string> RequiredScopes { get; set; } = new();
    public HashSet<string> RequiredClaims { get; set; } = new();
    public HashSet<string> AllowedNetworkZones { get; set; } = new();
    public bool AllowAnonymous { get; set; } = false;
}
