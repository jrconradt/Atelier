using Atelier.Build.Analysis;
using Atelier.Build.Discovery;
using Atelier.Build.Generation;
using Atelier.Build.Pipeline;
using Atelier.Build.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atelier.Build.Services;

public class BoutiqueGenerationService : IBoutiqueGenerationService
{
    private readonly ILogger<BoutiqueGenerationService> _logger;
    private readonly bool _verbose;

    public BoutiqueGenerationService(
        bool verbose = false,
        ILogger<BoutiqueGenerationService>? logger = null)
    {
        _verbose = verbose;
        _logger = logger ?? NullLogger<BoutiqueGenerationService>.Instance;
    }

    public async Task<GeneratedArtifacts> GenerateBoutiqueContainerAsync(
        BoutiqueYamlSchema schema,
        ProductDependencyGraph dependencyGraph,
        string outputDirectory,
        string solutionRoot,
        string compiledAssembliesDirectory,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating boutique container: {BoutiqueName}", schema.Name);

        var context = CreateBuildContext(solutionRoot);

        var resolved = BoutiqueResolver.Resolve(schema, dependencyGraph, compiledAssembliesDirectory, _verbose);

        var dockerfilePath = await GenerateDockerfileAsync(
            schema,
            dependencyGraph,
            resolved,
            outputDirectory,
            context,
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Generated Dockerfile for {BoutiqueName}", schema.Name);

        return new GeneratedArtifacts
        {
            DockerfilePath = dockerfilePath
        };
    }

    public async Task<string> GenerateDockerComposeAsync(
        IReadOnlyList<BoutiqueYamlSchema> schemas,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating docker-compose.yml for {Count} boutiques", schemas.Count);

        var context = CreateBuildContext(Path.GetDirectoryName(outputPath)!);
        var generator = new DockerComposeGenerator(context);

        var schemaList = schemas.ToList();
        var resolvedList = schemaList
            .Select(s => BoutiqueResolver.Resolve(s, new ProductDependencyGraph(), string.Empty, _verbose))
            .ToList();

        var path = await generator.GenerateAsync(schemaList, resolvedList, outputPath).ConfigureAwait(false);

        _logger.LogInformation("Generated docker-compose.yml: {Path}", path);

        return path;
    }

    public async Task<string> GenerateDiagramAsync(
        IReadOnlyList<BoutiqueManifest> boutiques,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating Mermaid diagram for {Count} boutiques", boutiques.Count);

        var context = CreateBuildContext(outputDirectory);

        var generator = new MermaidDiagramGenerator(context);
        var path = await generator.GenerateAsync(boutiques.ToList()).ConfigureAwait(false);

        _logger.LogInformation("Generated diagram: {Path}", path);

        return path;
    }

    private async Task<string> GenerateDockerfileAsync(
        BoutiqueYamlSchema schema,
        ProductDependencyGraph dependencyGraph,
        ResolvedBoutique resolved,
        string outputDirectory,
        Pipeline.BuildContext context,
        CancellationToken cancellationToken)
    {
        var generator = new DockerfileGenerator(context);
        return await generator.GenerateAsync(schema, dependencyGraph, resolved, outputDirectory).ConfigureAwait(false);
    }

    private Pipeline.BuildContext CreateBuildContext(string workingDirectory)
    {
        return new Pipeline.BuildContext
        {
            WorkingDirectory = workingDirectory,
            Verbose = _verbose
        };
    }
}
