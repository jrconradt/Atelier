using Atelier.Build.Analysis;
using Atelier.Build.Discovery;
using Atelier.Build.Pipeline;

namespace Atelier.Build.Services.Abstractions;

public interface IBoutiqueGenerationService
{
        public Task<GeneratedArtifacts> GenerateBoutiqueProjectAsync(
        BoutiqueYamlSchema schema,
        ProductDependencyGraph dependencyGraph,
        string outputDirectory,
        string solutionRoot,
        string compiledAssembliesDirectory,
        CancellationToken cancellationToken = default);

        public Task<string> GenerateDockerComposeAsync(
        IReadOnlyList<BoutiqueYamlSchema> schemas,
        string outputPath,
        CancellationToken cancellationToken = default);

        public Task<string> GenerateDiagramAsync(
        IReadOnlyList<BoutiqueManifest> boutiques,
        string outputDirectory,
        CancellationToken cancellationToken = default);
}

public record GeneratedArtifacts
{
    public string ProgramPath { get; init; } = string.Empty;
    public string ProjectPath { get; init; } = string.Empty;
    public string AssemblyLoaderPath { get; init; } = string.Empty;
    public string DockerfilePath { get; init; } = string.Empty;
    public IReadOnlyList<string> StubServicePaths { get; init; } = [];

    public IReadOnlyList<string> AllPaths =>
    [
        ProgramPath,
        ProjectPath,
        AssemblyLoaderPath,
        DockerfilePath,
        .. StubServicePaths
    ];
}
