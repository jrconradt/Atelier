using Atelier.Framework.Outcomes;
using Atelier.Framework.Host.Execution;

namespace Atelier.Framework.Facility;

public interface IFacility : IDisposable
{
    public string FacilityId { get; }
    public string FacilityName { get; }
    public FacilityType Type { get; }
    public FacilityCapabilities Capabilities { get; }

    public bool CanFulfill(IRequirement requirement);

    public Task<Outcome<ResourceAvailability>> CheckResourceAvailabilityAsync(
        ResourceAllocation requested,
        CancellationToken cancellationToken);

    public Task<Outcome<ProvisionTicket>> ProvisionAsync(
        IRequirement requirement,
        CancellationToken cancellationToken);

    public Task<Outcome> ReleaseAsync(
        string ticketId,
        CancellationToken cancellationToken);
}

public enum FacilityType
{
    InProcess,
    OutOfProcess,
    NetworkMapped,
    Hybrid
}
