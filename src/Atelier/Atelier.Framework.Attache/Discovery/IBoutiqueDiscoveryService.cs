using Atelier.Framework.Outcomes;

namespace Atelier.Framework.Attache.Discovery;

public interface IBoutiqueDiscoveryService
{
    public Task<Outcome<IEnumerable<AvailableBoutique>>> DiscoverBoutiquesAsync(
        CancellationToken cancellationToken = default);

    public Task<Outcome<AvailableBoutique>> DiscoverBoutiqueAsync(
        string boutiqueId,
        CancellationToken cancellationToken = default);

    public Task<Outcome> RegisterBoutiqueAsync(
        AvailableBoutique boutique,
        CancellationToken cancellationToken = default);

    public Task<Outcome> UnregisterBoutiqueAsync(
        string boutiqueId,
        CancellationToken cancellationToken = default);

    public Task<Outcome> UpdateEndpointsAsync(
        string boutiqueId,
        List<BoutiqueEndpoint> endpoints,
        CancellationToken cancellationToken = default);
}
