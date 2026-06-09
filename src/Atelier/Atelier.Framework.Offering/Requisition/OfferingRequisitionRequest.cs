using Atelier.Framework.Primitives;
using Atelier.Framework.Attributes;
using Atelier.Framework.Network;
using Atelier.Framework.Host.Execution;

namespace Atelier.Framework.Offering.Requisition;

[ContractAttribute(
    "OfferingRequisitionRequest",
    Version = "1.0",
    Namespace = "Framework.Offering.Requisition")]
public class OfferingRequisitionRequest
{
    public Type OfferingType { get; set; } = null!;

    public string RequesterId { get; set; } = string.Empty;

    public Type RequesterType { get; set; } = null!;

    public Type? RequesterZone { get; set; }

    public Type? TargetZone { get; set; }

    public ZonePlacementStrategy PlacementStrategy { get; set; } = ZonePlacementStrategy.SameZone;

    public OfferingExecutionMode PreferredExecutionMode { get; set; } = OfferingExecutionMode.InProcess;

    public ResourceAllocation? ResourceRequirements { get; set; }

    public BackingOfferingDescriptor? Backing { get; set; }

    public Dictionary<string, string> Configuration { get; set; } = new();

    public Dictionary<string, string> Metadata { get; set; } = new();

    public bool AllowSharedInstance { get; set; } = true;

    public TimeSpan? MaxWaitTime { get; set; }

    public RequisitionPriority Priority { get; set; } = RequisitionPriority.Normal;
}

public enum ZonePlacementStrategy
{
    SameZone,
    AllowCrossZone,
    RequireSpecificZone,
    AutoDetect
}

public enum RequisitionPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}
