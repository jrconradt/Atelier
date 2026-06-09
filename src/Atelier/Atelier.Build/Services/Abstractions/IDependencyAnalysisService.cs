using Atelier.Build.Analysis;
using Atelier.Build.Discovery;

namespace Atelier.Build.Services.Abstractions;

public interface IDependencyAnalysisService
{
        public Task<RequisiteAssemblies> AnalyzeRequisitesAsync(
        string assemblyPath,
        string outputDirectory,
        CancellationToken cancellationToken = default);

        public Task<ProductDependencyGraph> AnalyzeProductDependenciesAsync(
        IEnumerable<(string ProductType, string AssemblyName)> products,
        string compiledAssembliesDirectory,
        CancellationToken cancellationToken = default);

        public Task<DependencyGraph> BuildDependencyGraphAsync(
        IReadOnlyList<BoutiqueDefinition> definitions,
        CancellationToken cancellationToken = default);
}
