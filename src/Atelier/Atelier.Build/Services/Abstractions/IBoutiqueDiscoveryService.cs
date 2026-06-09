using Atelier.Build.Discovery;

namespace Atelier.Build.Services.Abstractions;

public interface IBoutiqueDiscoveryService
{
        public Task<IReadOnlyList<BoutiqueDefinition>> DiscoverBoutiquesAsync(
        string solutionRoot,
        CancellationToken cancellationToken = default);

        public Task<BoutiqueDefinition?> ParseBoutiqueAsync(
        string boutiqueYamlPath,
        CancellationToken cancellationToken = default);

        public Task<BoutiqueYamlSchema> ParseYamlSchemaAsync(
        string boutiqueYamlPath,
        CancellationToken cancellationToken = default);
}
