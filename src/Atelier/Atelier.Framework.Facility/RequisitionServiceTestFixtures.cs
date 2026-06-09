using Atelier.Framework.Host.Execution;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Testing;

namespace Atelier.Framework.Facility;

[TestFixtureRegistry]
internal static class RequisitionServiceTestFixtures
{
    [Fixture(typeof(IRequirement))]
    internal static IRequirement Requirement()
    {
        return new Requirement<object>
        {
            RequirementId = "fixture-requirement",
            Scope = RequirementScope.Offering
        };
    }

    [Fixture(typeof(IEnumerable<IFacility>))]
    internal static IEnumerable<IFacility> Facilities()
    {
        return new IFacility[] { new FixtureFacility() };
    }

    [Fixture(typeof(IFacilitySelector))]
    internal static IFacilitySelector Selector()
    {
        return new FixtureFacilitySelector();
    }

    private sealed class FixtureFacility : IFacility
    {
        public string FacilityId => "fixture-facility";
        public string FacilityName => "fixture-facility";
        public FacilityType Type => FacilityType.InProcess;
        public FacilityCapabilities Capabilities { get; } = BuildCapabilities();

        private static FacilityCapabilities BuildCapabilities()
        {
            var capabilities = new FacilityCapabilities();
            foreach (var scope in Enum.GetValues<RequirementScope>())
            {
                capabilities.SupportedScopes.Add(scope);
            }
            return capabilities;
        }

        public bool CanFulfill(IRequirement requirement)
        {
            return true;
        }

        public Task<Outcome<ResourceAvailability>> CheckResourceAvailabilityAsync(
            ResourceAllocation requested,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Outcome<ResourceAvailability>.Success(new ResourceAvailability
            {
                IsAvailable = true,
                Available = new ResourceAllocation()
            }));
        }

        public Task<Outcome<ProvisionTicket>> ProvisionAsync(
            IRequirement requirement,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Outcome<ProvisionTicket>.Success(new ProvisionTicket
            {
                TicketId = "fixture-ticket",
                RequirementId = requirement.RequirementId,
                FacilityId = FacilityId,
                Scope = requirement.Scope
            }));
        }

        public Task<Outcome> ReleaseAsync(
            string ticketId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Outcome.Success());
        }

        public void Dispose()
        {
        }
    }

    private sealed class FixtureFacilitySelector : IFacilitySelector
    {
        public Task<Outcome<IFacility>> SelectFacilityAsync(
            IRequirement requirement,
            IEnumerable<IFacility> availableFacilities,
            CancellationToken cancellationToken)
        {
            var facility = availableFacilities.FirstOrDefault();
            if (facility is null)
            {
                return Task.FromResult(Outcome<IFacility>.Failure());
            }
            return Task.FromResult(Outcome<IFacility>.Success(facility));
        }

        public Task<Outcome<IEnumerable<IFacility>>> FindFacilitiesByOperationAsync(
            string operationId,
            IEnumerable<IFacility> availableFacilities,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Outcome<IEnumerable<IFacility>>.Success(availableFacilities));
        }

        public Task<Outcome<IEnumerable<IFacility>>> FindFacilitiesByCapabilityAsync(
            CapabilityQuery query,
            IEnumerable<IFacility> availableFacilities,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Outcome<IEnumerable<IFacility>>.Success(availableFacilities));
        }
    }
}
