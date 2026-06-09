using Atelier.Framework.Outcomes;

namespace Atelier.Framework.Attache;

public interface IAttache
{
    public string InstanceId { get; }
    public AttacheState State { get; }

    public Outcome Configure(AttacheConfiguration configuration);

    public Task<Outcome<CapabilityGrant>> RequestCapabilityAsync(
        CapabilityRequest request,
        CancellationToken cancellationToken = default);

    public Task<Outcome> ReleaseCapabilityAsync(
        string ticketId,
        CancellationToken cancellationToken = default);

    public Task<Outcome> DeliverNoticeAsync(
        CapabilityNotice notice,
        CancellationToken cancellationToken = default);

    public IDisposable SubscribeNotices(Func<CapabilityNotice, CancellationToken, Task> handler);

    public Task<Outcome<AttacheHealthReport>> GetHealthReportAsync(
        CancellationToken cancellationToken = default);
}
