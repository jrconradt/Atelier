using Atelier.Build.Analysis;
using Atelier.Build.Discovery;
using Atelier.Build.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atelier.Build.Services;

public class DependencyAnalysisService : IDependencyAnalysisService
{
    private readonly ILogger<DependencyAnalysisService> _logger;
    private readonly bool _verbose;

    public DependencyAnalysisService(
        bool verbose = false,
        ILogger<DependencyAnalysisService>? logger = null)
    {
        _verbose = verbose;
        _logger = logger ?? NullLogger<DependencyAnalysisService>.Instance;
    }

    public Task<RequisiteAssemblies> AnalyzeRequisitesAsync(
        string assemblyPath,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation("Analyzing requisites for {AssemblyPath}", assemblyPath);

        var context = CreateBuildContext();
        var analyzer = new RequisiteAnalyzer(context);

        var requisiteAssemblies = analyzer.AnalyzeRequiredAssemblies(outputDirectory, assemblyPath);

        _logger.LogDebug("Found {Count} requisite assemblies", requisiteAssemblies.Count);

        return Task.FromResult(requisiteAssemblies);
    }

    public Task<ProductDependencyGraph> AnalyzeProductDependenciesAsync(
        IEnumerable<(string ProductType, string AssemblyName)> products,
        string compiledAssembliesDirectory,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation("Analyzing product dependencies");

        var context = CreateBuildContext();
        using var analyzer = new ProductDependencyAnalyzer(context);

        var dependencyGraph = analyzer.AnalyzeProducts(products, compiledAssembliesDirectory);

        _logger.LogInformation(
            "Analyzed {ProductCount} products: {AssemblyCount} assemblies, {TypeCount} types",
            products.Count(),
            dependencyGraph.TotalAssemblyCount,
            dependencyGraph.TypeCount);

        return Task.FromResult(dependencyGraph);
    }

    public Task<DependencyGraph> BuildDependencyGraphAsync(
        IReadOnlyList<BoutiqueDefinition> definitions,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation("Building dependency graph for {Count} boutiques", definitions.Count);

        var builder = new DependencyGraphBuilder();
        var graph = builder.Build(definitions);

        return Task.FromResult(graph);
    }

    private Pipeline.BuildContext CreateBuildContext()
    {
        return new Pipeline.BuildContext
        {
            WorkingDirectory = Directory.GetCurrentDirectory(),
            Verbose = _verbose
        };
    }
}
