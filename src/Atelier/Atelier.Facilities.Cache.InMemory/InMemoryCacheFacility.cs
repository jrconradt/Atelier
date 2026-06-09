using Atelier.Framework.Primitives;
using System.Collections.Concurrent;
using Atelier.Framework.Facility;
using Atelier.Framework.Attributes;
using Atelier.Framework.Network;
using Atelier.Framework.Observability;
using Atelier.Framework.Offering.Requisition;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;
using Atelier.Framework.Host.Execution;

using Atelier.Facilities.Cache;
namespace Atelier.Facilities.Cache.InMemory;

[Infrastructure(typeof(IFacility),
                typeof(InMemoryCacheFacility),
                InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class InMemoryCacheFacility : FacilityBase
{
    [Requisite] protected readonly IOfferingRequisitionService _offeringRequisitions = null!;

    private readonly ConcurrentDictionary<string, string> _ticketRequisitions = new();

    public override string FacilityId => "facility-cache-in-memory";
    public override string FacilityName => "In-Memory Cache Provider";
    public override FacilityType Type => FacilityType.InProcess;

    protected override void InitializeCapabilities()
    {
        Capabilities.Zone = typeof(Atelier.Framework.Primitives.Application);
        Capabilities.SupportedScopes.Add(RequirementScope.Capability);
        Capabilities.CanProvide.Add(typeof(ICache));
    }

    public override bool CanFulfill(IRequirement requirement)
    {
        if (!Capabilities.SupportedScopes.Contains(requirement.Scope))
        {
            return false;
        }

        return requirement.RequiredType == typeof(ICache);
    }

    public override Task<Outcome<ResourceAvailability>> CheckResourceAvailabilityAsync(
        ResourceAllocation requested,
        CancellationToken cancellationToken)
    {
        var availability = new ResourceAvailability
        {
            IsAvailable = true,
            Available = Capabilities.CurrentAvailable
        };

        return Task.FromResult(Outcome<ResourceAvailability>.Success(availability));
    }

    public override async Task<Outcome<ProvisionTicket>> ProvisionAsync(
        IRequirement requirement,
        CancellationToken cancellationToken)
    {
        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "Requirement", requirement.RequirementId);

        if (!CanFulfill(requirement))
        {
            Observe(LogLevel.Warning,
                    values: [("Reason", $"{FacilityName} cannot fulfill {requirement.RequiredType.Name}"), ("RequirementId", requirement.RequirementId)]);
            return Outcome<ProvisionTicket>.Failure();
        }

        var request = new OfferingRequisitionRequest
        {
            OfferingType = typeof(InMemoryCache),
            RequesterId = requirement.RequirementId,
            RequesterType = typeof(ICache),
            PreferredExecutionMode = OfferingExecutionMode.InProcess,
            PlacementStrategy = ZonePlacementStrategy.SameZone,
            ResourceRequirements = requirement.ResourceNeeds
        };

        var requisition = await _offeringRequisitions.RequisitionOfferingAsync(
            request,
            cancellationToken).ConfigureAwait(false);

        if (!requisition.IsSuccess)
        {
            Observe(LogLevel.Warning,
                    values: [("Reason", "Offering requisition did not succeed"), ("RequirementId", requirement.RequirementId)]);
            return Outcome<ProvisionTicket>.Failure();
        }

        var result = requisition.Data;

        var ticket = new ProvisionTicket
        {
            RequirementId = requirement.RequirementId,
            FacilityId = FacilityId,
            Scope = requirement.Scope,
            Zone = typeof(Atelier.Framework.Primitives.Application),
            AllocatedResources = result.AllocatedResources,
            Status = ProvisionStatus.Provisioned
        };

        _ticketRequisitions[ticket.TicketId] = result.RequisitionId;

        Observe(LogLevel.Information, values: [("RequirementId", requirement.RequirementId), ("TicketId", ticket.TicketId)]);

        return ticket;
    }

    public override async Task<Outcome> ReleaseAsync(
        string ticketId,
        CancellationToken cancellationToken)
    {
        if (_ticketRequisitions.TryRemove(ticketId, out var requisitionId))
        {
            var released = await _offeringRequisitions.ReleaseRequisitionAsync(
                requisitionId,
                cancellationToken).ConfigureAwait(false);

            if (!released.IsSuccess)
            {
                _ticketRequisitions.TryAdd(ticketId, requisitionId);
            }

            return released;
        }

        return Outcome.Success();
    }
}
