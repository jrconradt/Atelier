using Atelier.Framework.Primitives;
using Atelier.Framework.Attributes;
using Atelier.Framework.Network;
using Atelier.Framework.Host.Execution;

namespace Atelier.Framework.Offering.Requisition;

[ContractAttribute(
    "OfferingRequisitionResult",
    Version = "1.0",
    Namespace = "Framework.Offering.Requisition")]
public class OfferingRequisitionResult
{
    public string InstanceId { get; set; } = string.Empty;

    public string RequisitionId { get; set; } = string.Empty;

    public Type OfferingType { get; set; } = null!;

    public OfferingExecutionMode ExecutionMode { get; set; }

    public Type PlacedZone { get; set; } = null!;

    public bool IsSharedInstance { get; set; }

    public string? NetworkAddress { get; set; }

    public int? NetworkPort { get; set; }

    public int? ProcessId { get; set; }

    public ResourceAllocation? AllocatedResources { get; set; }

    public Dictionary<string, string> Metadata { get; set; } = new();

    public DateTime RequisitionedAt { get; set; } = DateTime.UtcNow;

    public RequisitionStatus Status { get; set; } = RequisitionStatus.Approved;
}

public enum RequisitionStatus
{
    Pending,
    Approved,
    Denied,
    Queued,
    Failed
}

[ContractAttribute(
    "RequisitionDenialReason",
    Version = "1.0",
    Namespace = "Framework.Offering.Requisition")]
public class RequisitionDenialReason
{
    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public RequisitionDenialType Type { get; set; }

    public Dictionary<string, object> Details { get; set; } = new();
}

public enum RequisitionDenialType
{
    InsufficientResources,
    ZoneViolation,
    OfferingNotFound,
    PermissionDenied,
    ConfigurationInvalid,
    SystemOverloaded
}
