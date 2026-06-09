using Atelier.Framework.Primitives;
using Atelier.Framework.Attributes;
using Atelier.Framework.Network;
using Atelier.Framework.Host.Execution;
using System.Text.Json.Serialization;

namespace Atelier.Framework.Facility;

[Contract("ProvisionTicket", Version = "1.0", Namespace = "Framework.Facility")]
public class ProvisionTicket
{
    public string TicketId { get; set; } = Guid.NewGuid().ToString();
    public string RequirementId { get; set; } = string.Empty;
    public string FacilityId { get; set; } = string.Empty;
    public RequirementScope Scope { get; set; }
    public string? GatewayEndpoint { get; set; }
    public int? GatewayPort { get; set; }
    public Type Zone { get; set; } = typeof(Atelier.Framework.Primitives.Application);

    [JsonIgnore]
    public Dictionary<string, string> Credentials { get; set; } = new();
    public ResourceAllocation? AllocatedResources { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
    public DateTime ProvisionedAt { get; set; } = DateTime.UtcNow;
    public ProvisionStatus Status { get; set; } = ProvisionStatus.Provisioned;
}

public enum ProvisionStatus
{
    Provisioned,
    Denied,
    Queued,
    Failed
}
