using Atelier.Framework.Attributes;
using Atelier.Framework.Primitives;
using Atelier.Framework.Context;
using Atelier.Framework.Network;
using Atelier.Framework.Offering.Requisition;
using Atelier.Framework.Infrastructure.Operation;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Host.Execution;

namespace Atelier.Framework.Offering;

public static class OfferingBaseExtensions
{
    public static async Task<Outcome<OfferingRequisitionResult>> RequisitionOfferingAsync<T>(
        this OfferingBase offeringBase,
        IOfferingRequisitionService requisitionService,
        ResourceAllocation? resourceRequirements = null,
        Type? targetZone = null,
        ZonePlacementStrategy placementStrategy = ZonePlacementStrategy.SameZone,
        OfferingExecutionMode executionMode = OfferingExecutionMode.InProcess,
        bool allowSharedInstance = true,
        CancellationToken cancellationToken = default) where T : class
    {
        var requesterZone = GetOfferingZone(offeringBase);

        var request = new OfferingRequisitionRequest
        {
            OfferingType = typeof(T),
            RequesterId = GetOfferingInstanceId(offeringBase),
            RequesterType = offeringBase.GetType(),
            RequesterZone = requesterZone,
            TargetZone = targetZone,
            PlacementStrategy = placementStrategy,
            PreferredExecutionMode = executionMode,
            ResourceRequirements = resourceRequirements,
            AllowSharedInstance = allowSharedInstance
        };

        return await requisitionService.RequisitionOfferingAsync<T>(
            request,
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task<Outcome<OfferingRequisitionResult>> RequisitionOfferingInSameZoneAsync<T>(
        this OfferingBase offeringBase,
        IOfferingRequisitionService requisitionService,
        ResourceAllocation? resourceRequirements = null,
        OfferingExecutionMode executionMode = OfferingExecutionMode.InProcess,
        CancellationToken cancellationToken = default) where T : class
    {
        return await RequisitionOfferingAsync<T>(
            offeringBase,
            requisitionService,
            resourceRequirements,
            null,
            ZonePlacementStrategy.SameZone,
            executionMode,
            true,
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task<Outcome<OfferingRequisitionResult>> RequisitionOfferingInZoneAsync<T>(
        this OfferingBase offeringBase,
        IOfferingRequisitionService requisitionService,
        Type targetZone,
        ResourceAllocation? resourceRequirements = null,
        OfferingExecutionMode executionMode = OfferingExecutionMode.InProcess,
        CancellationToken cancellationToken = default) where T : class
    {
        return await RequisitionOfferingAsync<T>(
            offeringBase,
            requisitionService,
            resourceRequirements,
            targetZone,
            ZonePlacementStrategy.RequireSpecificZone,
            executionMode,
            true,
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task<Outcome<OfferingRequisitionResult>> RequisitionDedicatedOfferingAsync<T>(
        this OfferingBase offeringBase,
        IOfferingRequisitionService requisitionService,
        ResourceAllocation? resourceRequirements = null,
        OfferingExecutionMode executionMode = OfferingExecutionMode.OutOfProcess,
        CancellationToken cancellationToken = default) where T : class
    {
        return await RequisitionOfferingAsync<T>(
            offeringBase,
            requisitionService,
            resourceRequirements,
            null,
            ZonePlacementStrategy.SameZone,
            executionMode,
            false,
            cancellationToken).ConfigureAwait(false);
    }

    private static Type? GetOfferingZone(OfferingBase offering)
    {
        var zoneAttr = offering.GetType()
            .GetCustomAttributes(typeof(NetworkZoneAttribute), false)
            .FirstOrDefault() as NetworkZoneAttribute;

        return zoneAttr?.Zone;
    }

    private static string GetOfferingInstanceId(OfferingBase offering)
    {
        return $"{offering.GetType().Name}_{Guid.NewGuid():N}";
    }
}
