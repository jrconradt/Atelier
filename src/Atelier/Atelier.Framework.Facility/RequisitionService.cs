using Atelier.Framework.Primitives;
using System.Collections.Concurrent;
using Atelier.Framework.Infrastructure;
using Atelier.Framework.Attributes;
using Atelier.Framework.Infrastructure.Operation;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;

namespace Atelier.Framework.Facility;

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class RequisitionService : IAtelier, IRequisitionService
{
    [Requisite] protected readonly IEnumerable<IFacility> _facilities = null!;
    [Requisite] protected readonly IFacilitySelector _facilitySelector = null!;

    private readonly ConcurrentDictionary<string, ActiveRequisition> _activeRequisitions = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<Outcome<ProvisionTicket>>>> _provisioningReservations = new();

    [Operation("ProvisionAsync")]
    public async Task<Outcome<ProvisionTicket>> ProvisionAsync(
        IRequirement requirement,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome<ProvisionTicket>.Failure();
        }

        if (requirement is null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Requirement was null")]);
            return Outcome<ProvisionTicket>.Failure();
        }

        if (_activeRequisitions.TryGetValue(requirement.RequirementId, out var existing))
        {
            return Outcome<ProvisionTicket>.Success(existing.Ticket);
        }

        var reservation = _provisioningReservations.GetOrAdd(
            requirement.RequirementId,
            _ => new Lazy<Task<Outcome<ProvisionTicket>>>(() => ProvisionOnceAsync(requirement, cancellationToken)));

        try
        {
            return await reservation.Value.ConfigureAwait(false);
        }
        finally
        {
            _provisioningReservations.TryRemove(requirement.RequirementId, out _);
        }
    }

    private async Task<Outcome<ProvisionTicket>> ProvisionOnceAsync(
        IRequirement requirement,
        CancellationToken cancellationToken)
    {
        if (_activeRequisitions.TryGetValue(requirement.RequirementId, out var existing))
        {
            return Outcome<ProvisionTicket>.Success(existing.Ticket);
        }

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "Requirement", requirement.RequirementId);

        Observe(LogLevel.Information, values: [("RequirementId", requirement.RequirementId), ("RequiredType", requirement.RequiredType.Name), ("Scope", requirement.Scope.ToString())]);

        var facilities = _facilities
            .Where(f => f.Capabilities.SupportedScopes.Contains(requirement.Scope))
            .Where(f => f.CanFulfill(requirement))
            .ToList();

        if (!facilities.Any())
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "No facilities available for requirement"), ("RequirementId", requirement.RequirementId), ("Scope", requirement.Scope.ToString())]);

            return Outcome<ProvisionTicket>.Failure();
        }

        var selectionResult = await _facilitySelector.SelectFacilityAsync(
            requirement,
            facilities,
            cancellationToken).ConfigureAwait(false);

        if (!selectionResult.IsSuccess)
        {
            return Outcome<ProvisionTicket>.Failure();
        }

        var facility = selectionResult.Data;
        var provisionResult = await facility.ProvisionAsync(requirement, cancellationToken).ConfigureAwait(false);

        if (!provisionResult.IsSuccess)
        {
            return Outcome<ProvisionTicket>.Failure();
        }

        var ticket = provisionResult.Data;

        var active = new ActiveRequisition
        {
            RequirementId = requirement.RequirementId,
            FurnishingId = ticket.TicketId,
            Scope = requirement.Scope,
            RequiredType = requirement.RequiredType,
            FacilityId = facility.FacilityId,
            AllocatedResources = ticket.AllocatedResources ?? new(),
            CreatedAt = DateTime.UtcNow,
            Ticket = ticket
        };

        _activeRequisitions[requirement.RequirementId] = active;

        Observe(LogLevel.Information, values: [("RequirementId", requirement.RequirementId), ("TicketId", ticket.TicketId), ("FacilityId", facility.FacilityId)]);

        return Outcome<ProvisionTicket>.Success(ticket);
    }

    [Operation("ReleaseAsync")]
    public async Task<Outcome> ReleaseAsync(
        string requirementId,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome.Failure();
        }

        if (requirementId is null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Requirement ID was null")]);
            return Outcome.Failure();
        }

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "Requirement", requirementId);

        Observe(LogLevel.Information, values: [("RequirementId", requirementId)]);

        if (!_activeRequisitions.TryRemove(requirementId, out var activeRequisition))
        {
            Observe(
                LogLevel.Information,
                null,
                values: [("Message", "Release of absent requisition treated as success"), ("RequirementId", requirementId)]);
            return Outcome.Success();
        }

        var facility = _facilities.FirstOrDefault(f => f.FacilityId == activeRequisition.FacilityId);
        if (facility == null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Facility not found for active requisition"), ("RequirementId", requirementId), ("FacilityId", activeRequisition.FacilityId)]);
            return Outcome.Failure();
        }

        var releaseResult = await facility.ReleaseAsync(
            activeRequisition.FurnishingId,
            cancellationToken).ConfigureAwait(false);

        if (!releaseResult.IsSuccess)
        {
            Observe(LogLevel.Warning, values: [("RequirementId", requirementId), ("FurnishingId", activeRequisition.FurnishingId)]);

            return releaseResult;
        }

        Observe(LogLevel.Information, values: [("RequirementId", requirementId), ("FurnishingId", activeRequisition.FurnishingId)]);

        return Outcome.Success();
    }

    public IEnumerable<ActiveRequisition> GetActiveRequisitions()
    {
        return _activeRequisitions.Values.ToList();
    }
}
