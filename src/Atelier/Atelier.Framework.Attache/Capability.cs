using System.Text.Json.Serialization;
using Atelier.Framework.Facility;
using Atelier.Framework.Attributes;
using Atelier.Framework.Host.Execution;

namespace Atelier.Framework.Attache;

[Contract("CapabilityRequest", Version = "1.0", Namespace = "Framework.Attache")]
public class CapabilityRequest
{
    public required string ConsumerId { get; set; }
    public required string CapabilityTypeName { get; set; }

    [JsonIgnore]
    public Type? CapabilityType { get; set; }

    public ResourceAllocation? ResourceNeeds { get; set; }
    public Dictionary<string, object> Constraints { get; set; } = new();
}

[Contract("CapabilityGrant", Version = "1.0", Namespace = "Framework.Attache")]
public class CapabilityGrant
{
    public required string ConsumerId { get; set; }
    public required string CapabilityName { get; set; }
    public string? GatewayEndpoint { get; set; }
    public int? GatewayPort { get; set; }

    [JsonIgnore]
    public Dictionary<string, string> Credentials { get; set; } = new();
    public string TicketId { get; set; } = string.Empty;
}

public enum CapabilityNoticeKind
{
    Provisioned,
    Updated,
    Released,
    Failed
}

[Contract("CapabilityNotice", Version = "1.0", Namespace = "Framework.Attache")]
public class CapabilityNotice
{
    public required string TicketId { get; set; }
    public required string CapabilityName { get; set; }
    public CapabilityNoticeKind Kind { get; set; }
    public string? GatewayEndpoint { get; set; }
    public string? Message { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

internal sealed class CapabilityRequirement : IRequirement
{
    public string RequirementId { get; set; } = Guid.NewGuid().ToString();
    public Type RequiredType { get; set; } = null!;
    public RequirementScope Scope { get; set; } = RequirementScope.Capability;
    public ResourceAllocation ResourceNeeds { get; set; } = new();
    public Dictionary<string, object> Constraints { get; set; } = new();
    public FulfillmentPreferences Preferences { get; set; } = new();
}
