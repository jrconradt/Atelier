using Atelier.Framework.Host.Execution;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Testing;

namespace Atelier.Framework.Facility;

public static class FacilitySelectionBehaviorTests
{
    private sealed class StubFacility : IFacility
    {
        public StubFacility(
            string id,
            FacilityType type,
            FacilityCapabilities capabilities)
        {
            FacilityId = id;
            FacilityName = id;
            Type = type;
            Capabilities = capabilities;
        }

        public string FacilityId { get; }
        public string FacilityName { get; }
        public FacilityType Type { get; }
        public FacilityCapabilities Capabilities { get; }

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
                Available = new ResourceAllocation { MaxMemoryBytes = 1_000_000_000 }
            }));
        }

        public Task<Outcome<ProvisionTicket>> ProvisionAsync(
            IRequirement requirement,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Outcome<ProvisionTicket>.Success(new ProvisionTicket
            {
                RequirementId = requirement.RequirementId,
                FacilityId = FacilityId
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

    private static StubFacility WithOperations(
        string id,
        FacilityType type,
        params string[] operations)
    {
        var caps = new FacilityCapabilities();
        foreach (var op in operations)
        {
            caps.Operations.Add(op);
        }
        return new StubFacility(id, type, caps);
    }

    [GeneratedTest("Facility/Select-Honors-Preferred-Facility-Id", "global::Atelier.Framework.Facility.FacilitySelector")]
    public static async Task SelectPrefersExplicitlyPreferredFacility()
    {
        var selector = new FacilitySelector(null);
        var preferred = WithOperations("fac-preferred", FacilityType.OutOfProcess, "op");
        var other = WithOperations("fac-other", FacilityType.OutOfProcess, "op");

        var requirement = new Requirement<object>();
        requirement.Preferences.PreferredFacilityId = "fac-preferred";

        var selected = await selector.SelectFacilityAsync(
            requirement,
            new IFacility[] { other, preferred },
            CancellationToken.None).ConfigureAwait(false);

        if (!selected.IsSuccess)
        {
            throw new InvalidOperationException("selection failed");
        }
        if (selected.Data!.FacilityId != "fac-preferred")
        {
            throw new InvalidOperationException($"expected fac-preferred, selected '{selected.Data.FacilityId}'");
        }
    }

    [GeneratedTest("Facility/Select-Internal-Only-Mode-Picks-InProcess", "global::Atelier.Framework.Facility.FacilitySelector")]
    public static async Task SelectInternalOnlyModePrefersInProcessFacility()
    {
        var selector = new FacilitySelector(null);
        var inProcess = WithOperations("fac-in", FacilityType.InProcess, "op");
        var external = WithOperations("fac-ext", FacilityType.OutOfProcess, "op");

        var requirement = new Requirement<object>();
        requirement.Preferences.Mode = FulfillmentMode.InternalOnly;

        var selected = await selector.SelectFacilityAsync(
            requirement,
            new IFacility[] { external, inProcess },
            CancellationToken.None).ConfigureAwait(false);

        if (!selected.IsSuccess)
        {
            throw new InvalidOperationException("selection failed");
        }
        if (selected.Data!.FacilityId != "fac-in")
        {
            throw new InvalidOperationException($"InternalOnly mode selected '{selected.Data.FacilityId}', expected fac-in");
        }
    }

    [GeneratedTest("Facility/Select-Empty-Set-Fails", "global::Atelier.Framework.Facility.FacilitySelector")]
    public static async Task SelectFailsWhenNoFacilitiesAvailable()
    {
        var selector = new FacilitySelector(null);

        var selected = await selector.SelectFacilityAsync(
            new Requirement<object>(),
            Array.Empty<IFacility>(),
            CancellationToken.None).ConfigureAwait(false);

        if (selected.IsSuccess)
        {
            throw new InvalidOperationException("selection succeeded against an empty facility set");
        }
    }

    [GeneratedTest("Facility/Find-By-Capability-Require-All", "global::Atelier.Framework.Facility.FacilitySelector")]
    public static async Task FindByCapabilityRequireAllMatchesOnlyFacilitiesWithEveryOperation()
    {
        var selector = new FacilitySelector(null);
        var both = WithOperations("fac-both", FacilityType.InProcess, "read", "write");
        var readOnly = WithOperations("fac-read", FacilityType.InProcess, "read");

        var query = new CapabilityQuery
        {
            RequireAll = true,
            RequiredOperations = new HashSet<string> { "read", "write" }
        };

        var result = await selector.FindFacilitiesByCapabilityAsync(
            query,
            new IFacility[] { both, readOnly },
            CancellationToken.None).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException("capability query failed");
        }
        var matches = result.Data!.ToList();
        if (matches.Count != 1
            || matches[0].FacilityId != "fac-both")
        {
            throw new InvalidOperationException($"RequireAll query matched {matches.Count} facilities, expected only fac-both");
        }
    }

    [GeneratedTest("Facility/Find-By-Capability-Require-Any", "global::Atelier.Framework.Facility.FacilitySelector")]
    public static async Task FindByCapabilityRequireAnyMatchesFacilitiesWithSomeOperation()
    {
        var selector = new FacilitySelector(null);
        var both = WithOperations("fac-both", FacilityType.InProcess, "read", "write");
        var readOnly = WithOperations("fac-read", FacilityType.InProcess, "read");

        var query = new CapabilityQuery
        {
            RequireAll = false,
            RequiredOperations = new HashSet<string> { "read", "write" }
        };

        var result = await selector.FindFacilitiesByCapabilityAsync(
            query,
            new IFacility[] { both, readOnly },
            CancellationToken.None).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException("capability query failed");
        }
        if (result.Data!.Count() != 2)
        {
            throw new InvalidOperationException($"RequireAll=false query matched {result.Data.Count()} facilities, expected both");
        }
    }
}
