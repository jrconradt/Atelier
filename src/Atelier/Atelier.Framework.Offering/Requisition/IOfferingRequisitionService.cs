using Atelier.Framework.Context;
using Atelier.Framework.Infrastructure.Operation;
using Atelier.Framework.Outcomes;

namespace Atelier.Framework.Offering.Requisition;

public interface IOfferingRequisitionService
{
    public Task<Outcome<OfferingRequisitionResult>> RequisitionOfferingAsync<T>(
        OfferingRequisitionRequest request,
        CancellationToken cancellationToken = default) where T : class;

    public Task<Outcome<OfferingRequisitionResult>> RequisitionOfferingAsync(
        OfferingRequisitionRequest request,
        CancellationToken cancellationToken = default);

    public Task<Outcome> ReleaseRequisitionAsync(
        string requisitionId,
        CancellationToken cancellationToken = default);

    public Task<Outcome<RequisitionInfo>> GetRequisitionInfoAsync(
        string requisitionId,
        CancellationToken cancellationToken = default);

    public Task<Outcome<List<RequisitionInfo>>> GetRequisitionsByRequesterAsync(
        string requesterId,
        CancellationToken cancellationToken = default);

    public IOfferingHandle<T> CreateHandle<T>(OfferingRequisitionResult result) where T : class;
}

public class RequisitionInfo
{
    public string RequisitionId { get; set; } = string.Empty;

    public string InstanceId { get; set; } = string.Empty;

    public string RequesterId { get; set; } = string.Empty;

    public Type RequesterType { get; set; } = null!;

    public Type OfferingType { get; set; } = null!;

    public RequisitionStatus Status { get; set; }

    public DateTime RequisitionedAt { get; set; }

    public DateTime? ReleasedAt { get; set; }

    public bool IsShared { get; set; }

    public int ReferenceCount { get; set; }
}
