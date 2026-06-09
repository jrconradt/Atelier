using Atelier.Framework.Host.Execution;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Testing;

namespace Atelier.Framework.Facility;

public static class RequisitionServiceBehaviorTests
{
    private sealed class CountingFacility : IFacility
    {
        public int ProvisionCalls;
        public int ReleaseCalls;
        public string? LastReleasedTicketId;

        public CountingFacility(
            string id,
            RequirementScope scope)
        {
            FacilityId = id;
            FacilityName = id;
            Type = FacilityType.InProcess;
            Capabilities = new FacilityCapabilities();
            Capabilities.SupportedScopes.Add(scope);
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
            ProvisionCalls++;
            return Task.FromResult(Outcome<ProvisionTicket>.Success(new ProvisionTicket
            {
                TicketId = $"{FacilityId}-ticket",
                RequirementId = requirement.RequirementId,
                FacilityId = FacilityId,
                Scope = requirement.Scope
            }));
        }

        public Task<Outcome> ReleaseAsync(
            string ticketId,
            CancellationToken cancellationToken)
        {
            ReleaseCalls++;
            LastReleasedTicketId = ticketId;
            return Task.FromResult(Outcome.Success());
        }

        public void Dispose()
        {
        }
    }

    private static RequisitionService BuildService(params IFacility[] facilities)
    {
        return new RequisitionService(facilities, new FacilitySelector(null), null);
    }

    [GeneratedTest("Facility/Provision-Registers-Active-Requisition", "global::Atelier.Framework.Facility.RequisitionService")]
    public static async Task ProvisionRecordsActiveRequisition()
    {
        var facility = new CountingFacility("fac-a", RequirementScope.Offering);
        var service = BuildService(facility);

        var requirement = new Requirement<object>
        {
            Scope = RequirementScope.Offering
        };

        var provision = await service.ProvisionAsync(
            requirement,
            CancellationToken.None).ConfigureAwait(false);

        if (!provision.IsSuccess)
        {
            throw new InvalidOperationException("provision failed");
        }
        if (provision.Data!.FacilityId != "fac-a")
        {
            throw new InvalidOperationException($"ticket bound to '{provision.Data.FacilityId}', expected fac-a");
        }

        var active = service.GetActiveRequisitions().ToList();
        if (active.Count != 1)
        {
            throw new InvalidOperationException($"active requisition count {active.Count}, expected 1");
        }
        if (active[0].RequirementId != requirement.RequirementId)
        {
            throw new InvalidOperationException($"active requisition records '{active[0].RequirementId}', expected '{requirement.RequirementId}'");
        }
        if (active[0].FacilityId != "fac-a")
        {
            throw new InvalidOperationException($"active requisition facility '{active[0].FacilityId}', expected fac-a");
        }
    }

    [GeneratedTest("Facility/Provision-Is-Idempotent-Per-Requirement", "global::Atelier.Framework.Facility.RequisitionService")]
    public static async Task ProvisionTwiceReusesExistingTicket()
    {
        var facility = new CountingFacility("fac-a", RequirementScope.Offering);
        var service = BuildService(facility);

        var requirement = new Requirement<object>
        {
            Scope = RequirementScope.Offering
        };

        var first = await service.ProvisionAsync(
            requirement,
            CancellationToken.None).ConfigureAwait(false);
        var second = await service.ProvisionAsync(
            requirement,
            CancellationToken.None).ConfigureAwait(false);

        if (!first.IsSuccess
            || !second.IsSuccess)
        {
            throw new InvalidOperationException("provision failed");
        }
        if (first.Data!.TicketId != second.Data!.TicketId)
        {
            throw new InvalidOperationException($"second provision returned '{second.Data.TicketId}', expected reuse of '{first.Data.TicketId}'");
        }
        if (facility.ProvisionCalls != 1)
        {
            throw new InvalidOperationException($"facility provisioned {facility.ProvisionCalls} times, expected 1");
        }
        if (service.GetActiveRequisitions().Count() != 1)
        {
            throw new InvalidOperationException($"active requisition count {service.GetActiveRequisitions().Count()}, expected 1");
        }
    }

    [GeneratedTest("Facility/Provision-No-Matching-Scope-Fails", "global::Atelier.Framework.Facility.RequisitionService")]
    public static async Task ProvisionFailsWhenNoFacilitySupportsScope()
    {
        var facility = new CountingFacility("fac-a", RequirementScope.Offering);
        var service = BuildService(facility);

        var requirement = new Requirement<object>
        {
            Scope = RequirementScope.Boutique
        };

        var provision = await service.ProvisionAsync(
            requirement,
            CancellationToken.None).ConfigureAwait(false);

        if (provision.IsSuccess)
        {
            throw new InvalidOperationException("provision succeeded with no scope-matching facility");
        }
        if (service.GetActiveRequisitions().Any())
        {
            throw new InvalidOperationException("failed provision left an active requisition behind");
        }
    }

    [GeneratedTest("Facility/Release-Removes-Active-And-Releases-Facility", "global::Atelier.Framework.Facility.RequisitionService")]
    public static async Task ReleaseRemovesActiveRequisitionAndReleasesFacility()
    {
        var facility = new CountingFacility("fac-a", RequirementScope.Offering);
        var service = BuildService(facility);

        var requirement = new Requirement<object>
        {
            Scope = RequirementScope.Offering
        };

        var provision = await service.ProvisionAsync(
            requirement,
            CancellationToken.None).ConfigureAwait(false);
        if (!provision.IsSuccess)
        {
            throw new InvalidOperationException("provision failed");
        }

        var release = await service.ReleaseAsync(
            requirement.RequirementId,
            CancellationToken.None).ConfigureAwait(false);

        if (!release.IsSuccess)
        {
            throw new InvalidOperationException("release failed");
        }
        if (facility.ReleaseCalls != 1)
        {
            throw new InvalidOperationException($"facility released {facility.ReleaseCalls} times, expected 1");
        }
        if (facility.LastReleasedTicketId != "fac-a-ticket")
        {
            throw new InvalidOperationException($"released ticket '{facility.LastReleasedTicketId}', expected fac-a-ticket");
        }
        if (service.GetActiveRequisitions().Any())
        {
            throw new InvalidOperationException("release left an active requisition behind");
        }
    }

    [GeneratedTest("Facility/Release-Unknown-Requirement-Is-Idempotent", "global::Atelier.Framework.Facility.RequisitionService")]
    public static async Task ReleaseUnknownRequirementSucceeds()
    {
        var facility = new CountingFacility("fac-a", RequirementScope.Offering);
        var service = BuildService(facility);

        var release = await service.ReleaseAsync(
            "never-provisioned",
            CancellationToken.None).ConfigureAwait(false);

        if (!release.IsSuccess)
        {
            throw new InvalidOperationException("release of an absent requirement reported failure");
        }
        if (facility.ReleaseCalls != 0)
        {
            throw new InvalidOperationException($"facility released {facility.ReleaseCalls} times for an unknown requirement, expected 0");
        }
    }

    [GeneratedTest("Facility/Release-Repeated-Is-Idempotent", "global::Atelier.Framework.Facility.RequisitionService")]
    public static async Task ReleaseRepeatedSucceeds()
    {
        var facility = new CountingFacility("fac-a", RequirementScope.Offering);
        var service = BuildService(facility);

        var requirement = new Requirement<object>
        {
            Scope = RequirementScope.Offering
        };

        var provision = await service.ProvisionAsync(
            requirement,
            CancellationToken.None).ConfigureAwait(false);
        if (!provision.IsSuccess)
        {
            throw new InvalidOperationException("provision failed");
        }

        var first = await service.ReleaseAsync(
            requirement.RequirementId,
            CancellationToken.None).ConfigureAwait(false);
        var second = await service.ReleaseAsync(
            requirement.RequirementId,
            CancellationToken.None).ConfigureAwait(false);

        if (!first.IsSuccess
            || !second.IsSuccess)
        {
            throw new InvalidOperationException("repeated release failed");
        }
        if (facility.ReleaseCalls != 1)
        {
            throw new InvalidOperationException($"facility released {facility.ReleaseCalls} times across a repeated release, expected 1");
        }
    }

    [GeneratedTest("Facility/Provision-Cancelled-Token-Fails", "global::Atelier.Framework.Facility.RequisitionService")]
    public static async Task ProvisionWithCancelledTokenFails()
    {
        var facility = new CountingFacility("fac-a", RequirementScope.Offering);
        var service = BuildService(facility);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var provision = await service.ProvisionAsync(
            new Requirement<object> { Scope = RequirementScope.Offering },
            cts.Token).ConfigureAwait(false);

        if (provision.IsSuccess)
        {
            throw new InvalidOperationException("provision succeeded against a cancelled token");
        }
        if (facility.ProvisionCalls != 0)
        {
            throw new InvalidOperationException($"facility provisioned {facility.ProvisionCalls} times under cancellation, expected 0");
        }
    }
}
