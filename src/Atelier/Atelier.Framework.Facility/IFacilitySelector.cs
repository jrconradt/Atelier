using Atelier.Framework.Outcomes;

namespace Atelier.Framework.Facility;

public interface IFacilitySelector
{
    public Task<Outcome<IFacility>> SelectFacilityAsync(
        IRequirement requirement,
        IEnumerable<IFacility> availableFacilities,
        CancellationToken cancellationToken);

    public Task<Outcome<IEnumerable<IFacility>>> FindFacilitiesByOperationAsync(
        string operationId,
        IEnumerable<IFacility> availableFacilities,
        CancellationToken cancellationToken);

    public Task<Outcome<IEnumerable<IFacility>>> FindFacilitiesByCapabilityAsync(
        CapabilityQuery query,
        IEnumerable<IFacility> availableFacilities,
        CancellationToken cancellationToken);
}
