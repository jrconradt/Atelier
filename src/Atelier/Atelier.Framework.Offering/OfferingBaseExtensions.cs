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
        OfferingExecutionMode executionMode = OfferingExecutionMode.InProcess,
        bool allowSharedInstance = true,
        CancellationToken cancellationToken = default) where T : class
    {
        var request = new OfferingRequisitionRequest
        {
            OfferingType = typeof(T),
            RequesterId = GetOfferingInstanceId(offeringBase),
            RequesterType = offeringBase.GetType(),
            PreferredExecutionMode = executionMode,
            ResourceRequirements = resourceRequirements,
            AllowSharedInstance = allowSharedInstance
        };

        return await requisitionService.RequisitionOfferingAsync<T>(
            request,
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
            executionMode,
            false,
            cancellationToken).ConfigureAwait(false);
    }

    private static string GetOfferingInstanceId(OfferingBase offering)
    {
        return $"{offering.GetType().Name}_{Guid.NewGuid():N}";
    }
}
