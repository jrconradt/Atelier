using Atelier.Framework.Primitives;
using Atelier.Framework.Network;

namespace Atelier.Framework.Offering.Discovery;

public class OfferingAnnouncement
{
    public string InstanceId { get; set; } = string.Empty;
    public string OfferingTypeName { get; set; } = string.Empty;
    public string? NetworkAddress { get; set; }
    public int? NetworkPort { get; set; }
    public Type? Zone { get; set; }
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
    public Dictionary<string, string> Metadata { get; set; } = new();
    public List<string>? RequiredScopes { get; set; }
    public List<string>? RequiredClaims { get; set; }
    public List<OfferingContract> Contracts { get; set; } = new();
}
