using Atelier.Build.Discovery;

namespace Atelier.Build.Services.Abstractions;

public interface IDependencyAnalysisService
{
        public Task<DependencyGraph> BuildDependencyGraphAsync(
        IReadOnlyList<BoutiqueDefinition> definitions,
        CancellationToken cancellationToken = default);
}
