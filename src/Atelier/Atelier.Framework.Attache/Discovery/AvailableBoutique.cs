namespace Atelier.Framework.Attache.Discovery;

public class AvailableBoutique
{
    public required string BoutiqueId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Version { get; set; }
    public BoutiqueState State { get; set; }
    public List<BoutiqueEndpoint> Endpoints { get; set; } = new();
    public BoutiqueUiMetadata? UiMetadata { get; set; }
    public BoutiqueCapabilities Capabilities { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new();
    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;
}
