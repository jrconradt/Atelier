using Atelier.Framework.Attributes;

namespace Atelier.Framework.Facility.Remote;

[Contract("RemoteFacilityDescriptor", Version = "1.0")]
public class RemoteFacilityDescriptor
{
    public string FacilityId { get; set; } = string.Empty;
    public string FacilityName { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public FacilityType Type { get; set; } = FacilityType.NetworkMapped;
    public FacilityCapabilities Capabilities { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new();
    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;
    public DateTime LastHealthCheck { get; set; } = DateTime.UtcNow;
    public bool IsHealthy { get; set; } = true;
}
