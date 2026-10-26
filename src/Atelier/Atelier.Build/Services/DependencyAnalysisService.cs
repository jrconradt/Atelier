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
