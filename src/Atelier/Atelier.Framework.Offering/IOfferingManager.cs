using Atelier.Framework.Offering.Discovery;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Host.Execution;

namespace Atelier.Framework.Offering;

public interface IOfferingManager
{
    public OfferingInstanceDescriptor? GetOfferingDescriptor(string instanceId);
    public IEnumerable<OfferingInstanceDescriptor> GetAllOfferings();
    public Task<Outcome<string>> StartOffering(Type offeringType, OfferingStartOptions options);
    public Task<Outcome> StopOffering(string instanceId);
    public Task<Outcome<string>> StartOfferingAsync(Type offeringType, OfferingStartOptions options, CancellationToken cancellationToken = default);
    public Task<Outcome> StopOfferingAsync(string instanceId, CancellationToken cancellationToken = default);
    public IEnumerable<OfferingInstanceDescriptor> GetOfferingsByType(Type offeringType);
    public IEnumerable<OfferingAnnouncement> DiscoverNetworkOfferings(string? offeringTypeName = null);
    public Outcome UpdateOfferingHeartbeat(string instanceId);
}
