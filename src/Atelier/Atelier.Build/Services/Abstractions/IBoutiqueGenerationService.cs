using Atelier.Build.Discovery;
using Atelier.Build.Generation;
using Atelier.Build.Pipeline;

namespace Atelier.Build.Services.Abstractions;

public interface IBoutiqueGenerationService
{
        public Task<GeneratedArtifacts> GenerateBoutiqueContainerAsync(
        BoutiqueYamlSchema schema,
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
    public string DockerfilePath { get; init; } = string.Empty;

    public IReadOnlyList<string> AllPaths =>
    [
        DockerfilePath
    ];
}
