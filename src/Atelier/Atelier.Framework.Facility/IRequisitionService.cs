using Atelier.Framework.Outcomes;

namespace Atelier.Framework.Facility;

public interface IRequisitionService
{
    public Task<Outcome<ProvisionTicket>> ProvisionAsync(
        IRequirement requirement,
        CancellationToken cancellationToken);

    public Task<Outcome> ReleaseAsync(
        string requirementId,
        CancellationToken cancellationToken);

    public IEnumerable<ActiveRequisition> GetActiveRequisitions();
}
