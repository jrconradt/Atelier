using Atelier.Framework.Primitives;
using System.Collections.Concurrent;
using Atelier.Framework.Infrastructure;
using Atelier.Framework.Attributes;
using Atelier.Framework.Identity.Authorization;
using Atelier.Framework.Infrastructure.Operation;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;

namespace Atelier.Framework.Attache.Discovery;

[Infrastructure(InfrastructureLifetime.Singleton)]
[Api(claims: new[] { Claims.BOUTIQUE_READ })]
[ScopeResource(typeof(Scopes.Boutique))]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class BoutiqueDiscoveryService : IAtelier, IBoutiqueDiscoveryService
{
    private readonly ConcurrentDictionary<string, AvailableBoutique> _boutiques = new();

    [Operation("DiscoverBoutiquesAsync")]
    [OperationEffect(EffectKind.Read)]
    public Task<Outcome<IEnumerable<AvailableBoutique>>> DiscoverBoutiquesAsync(
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Outcome<IEnumerable<AvailableBoutique>>.Failure());
        }

        var boutiques = _boutiques.Values.ToList();

        Observe(LogLevel.Information, values: [("Count", boutiques.Count)]);

        return Task.FromResult(Outcome<IEnumerable<AvailableBoutique>>.Success(boutiques));
    }

    [Operation("DiscoverBoutiqueAsync")]
    [OperationEffect(EffectKind.Read)]
    public Task<Outcome<AvailableBoutique>> DiscoverBoutiqueAsync(
        string boutiqueId,
        CancellationToken cancellationToken = default)
    {
        if (boutiqueId is null || string.IsNullOrWhiteSpace(boutiqueId))
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", "Boutique ID was null or empty")]);
            return Task.FromResult(Outcome<AvailableBoutique>.Failure());
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Outcome<AvailableBoutique>.Failure());
        }


        if (!_boutiques.TryGetValue(boutiqueId, out var boutique))
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", "Boutique not found"), ("BoutiqueId", boutiqueId)]);
            return Task.FromResult(Outcome<AvailableBoutique>.Failure());
        }

        return Task.FromResult(Outcome<AvailableBoutique>.Success(boutique));
    }

    [Operation("RegisterBoutiqueAsync")]
    [OperationEffect(EffectKind.Write)]
    public Task<Outcome> RegisterBoutiqueAsync(
        AvailableBoutique boutique,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Outcome.Failure());
        }

        ArgumentNullException.ThrowIfNull(boutique);

        _boutiques[boutique.BoutiqueId] = boutique;

        Observe(LogLevel.Information, values: [("BoutiqueId", boutique.BoutiqueId), ("Name", boutique.Name), ("EndpointCount", boutique.Endpoints.Count)]);

        return Task.FromResult(Outcome.Success());
    }

    [Operation("UnregisterBoutiqueAsync")]
    [OperationEffect(EffectKind.Write)]
    public Task<Outcome> UnregisterBoutiqueAsync(
        string boutiqueId,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Outcome.Failure());
        }


        if (!_boutiques.TryRemove(boutiqueId, out _))
        {
            Observe(
                LogLevel.Information,
                values: [("Message", "Unregister of absent boutique treated as success"), ("BoutiqueId", boutiqueId)]);
            return Task.FromResult(Outcome.Success());
        }

        Observe(LogLevel.Information, values: [("BoutiqueId", boutiqueId)]);

        return Task.FromResult(Outcome.Success());
    }

    [Operation("UpdateEndpointsAsync")]
    [OperationEffect(EffectKind.Write)]
    public Task<Outcome> UpdateEndpointsAsync(
        string boutiqueId,
        List<BoutiqueEndpoint> endpoints,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Outcome.Failure());
        }

        if (endpoints is null)
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", "Endpoints were null"), ("BoutiqueId", boutiqueId)]);
            return Task.FromResult(Outcome.Failure());
        }


        if (!_boutiques.TryGetValue(boutiqueId, out var boutique))
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", "Boutique not found"), ("BoutiqueId", boutiqueId)]);
            return Task.FromResult(Outcome.Failure());
        }

        _boutiques[boutiqueId] = new AvailableBoutique
        {
            BoutiqueId = boutique.BoutiqueId,
            Name = boutique.Name,
            Description = boutique.Description,
            Version = boutique.Version,
            State = boutique.State,
            Endpoints = endpoints,
            UiMetadata = boutique.UiMetadata,
            Capabilities = boutique.Capabilities,
            Metadata = boutique.Metadata
        };

        Observe(LogLevel.Information, values: [("BoutiqueId", boutiqueId), ("EndpointCount", endpoints.Count)]);

        return Task.FromResult(Outcome.Success());
    }
}
