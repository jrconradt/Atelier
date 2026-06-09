using Atelier.Build.Pipeline;

namespace Atelier.Build.Services.Abstractions;

public interface IArtifactManagementService
{
        public Task<string> GenerateRequisiteManifestAsync(
        IReadOnlyList<BoutiqueManifest> boutiques,
        string outputDirectory,
        CancellationToken cancellationToken = default);

        public Task<string> GenerateAssemblyLoaderAsync(
        IReadOnlyList<BoutiqueManifest> boutiques,
        string outputDirectory,
        string assembliesDirectory,
        CancellationToken cancellationToken = default);

        public Task EnforceRetentionPolicyAsync(
        string artifactsDirectory,
        int maxRetainedBuilds = 5,
        CancellationToken cancellationToken = default);

        public Task<CleanupResult> CleanArtifactsAsync(
        string artifactsDirectory,
        bool cleanAll = false,
        bool cleanDocker = false,
        CancellationToken cancellationToken = default);
}

public record CleanupResult
{
    public int FilesDeleted { get; init; }
    public int DirectoriesDeleted { get; init; }
    public int DockerImagesRemoved { get; init; }
    public long BytesFreed { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}
