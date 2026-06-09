using Atelier.Framework.Host.Execution;

namespace Atelier.Framework.Facility;

public class ActiveRequisition
{
    public string RequirementId { get; set; } = string.Empty;
    public string FurnishingId { get; set; } = string.Empty;
    public RequirementScope Scope { get; set; }
    public Type RequiredType { get; set; } = null!;
    public string FacilityId { get; set; } = string.Empty;
    public ResourceAllocation AllocatedResources { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public ProvisionTicket Ticket { get; set; } = new();
}
