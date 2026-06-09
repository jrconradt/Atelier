using System.Diagnostics;
using System.Text.Json;
using Atelier.Build.Generation;
using Atelier.Build.Pipeline;
using Atelier.Build.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atelier.Build.Services;

public class ArtifactManagementService : IArtifactManagementService
{
    private static readonly TimeSpan DockerCommandTimeout = TimeSpan.FromMinutes(5);
    private readonly ILogger<ArtifactManagementService> _logger;
    private readonly bool _verbose;

    public ArtifactManagementService(
        bool verbose = false,
        ILogger<ArtifactManagementService>? logger = null)
    {
        _verbose = verbose;
        _logger = logger ?? NullLogger<ArtifactManagementService>.Instance;
    }

    public async Task<string> GenerateRequisiteManifestAsync(
        IReadOnlyList<BoutiqueManifest> boutiques,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating requisite manifest for {Count} boutiques", boutiques.Count);

        var manifest = boutiques.ToDictionary(
            b => b.Name,
            b => new
            {
                b.OutputAssembly,
                b.Offerings,
                b.Dependencies,
                b.RequisiteAssemblies
            });

        var outputPath = Path.Combine(outputDirectory, "requisite-manifest.json");

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var json = JsonSerializer.Serialize(manifest, options);
        await File.WriteAllTextAsync(outputPath, json, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Generated requisite manifest: {Path}", outputPath);

        return outputPath;
    }

    public async Task<string> GenerateAssemblyLoaderAsync(
        IReadOnlyList<BoutiqueManifest> boutiques,
        string outputDirectory,
        string assembliesDirectory,
        CancellationToken cancellationToken = default)
    {

        await Task.CompletedTask.ConfigureAwait(false);
        return string.Empty;
    }

    public async Task EnforceRetentionPolicyAsync(
        string artifactsDirectory,
        int maxRetainedBuilds = 5,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Enforcing retention policy: max {MaxBuilds} builds", maxRetainedBuilds);

        var context = CreateBuildContext(artifactsDirectory);
        var manager = new ArtifactRetentionManager(context);

        await manager.EnforceRetentionAsync().ConfigureAwait(false);

        _logger.LogInformation("Retention policy enforced");
    }

    public async Task<CleanupResult> CleanArtifactsAsync(
        string artifactsDirectory,
        bool cleanAll = false,
        bool cleanDocker = false,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Cleaning artifacts: cleanAll={CleanAll}, cleanDocker={CleanDocker}",
            cleanAll,
            cleanDocker);

        try
        {
            int filesDeleted = 0;
            int directoriesDeleted = 0;
            long bytesFreed = 0;

            if (Directory.Exists(artifactsDirectory))
            {
                var files = Directory.GetFiles(artifactsDirectory, "*", SearchOption.AllDirectories);

                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var fileInfo = new FileInfo(file);
                        bytesFreed += fileInfo.Length;
                        File.Delete(file);
                        filesDeleted++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete file: {File}", file);
                    }
                }

                if (cleanAll)
                {
                    var directories = Directory.GetDirectories(artifactsDirectory, "*", SearchOption.AllDirectories)
                        .OrderByDescending(d => d.Length);

                    foreach (var dir in directories)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            Directory.Delete(dir, recursive: true);
                            directoriesDeleted++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete directory: {Directory}", dir);
                        }
                    }
                }
            }

            int dockerImagesRemoved = 0;

            if (cleanDocker)
            {
                dockerImagesRemoved = await CleanDockerImagesAsync(cancellationToken).ConfigureAwait(false);
            }

            _logger.LogInformation(
                "Cleanup complete: {FilesDeleted} files, {DirectoriesDeleted} directories, {BytesFreed} bytes freed",
                filesDeleted,
                directoriesDeleted,
                bytesFreed);

            return new CleanupResult
            {
                FilesDeleted = filesDeleted,
                DirectoriesDeleted = directoriesDeleted,
                DockerImagesRemoved = dockerImagesRemoved,
                BytesFreed = bytesFreed,
                Success = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cleanup failed");

            return new CleanupResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private async Task<int> CleanDockerImagesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var listProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "images atelier-* -q",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (listProcess == null)
            {
                _logger.LogWarning("Failed to start docker process");
                return 0;
            }

            using var listCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            listCts.CancelAfter(DockerCommandTimeout);

            string output;

            try
            {
                output = await listProcess.StandardOutput.ReadToEndAsync(listCts.Token).ConfigureAwait(false);
                await listProcess.WaitForExitAsync(listCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (listCts.IsCancellationRequested
                                                     && !cancellationToken.IsCancellationRequested)
            {
                KillProcessTree(listProcess);
                _logger.LogWarning("docker images timed out after {Timeout}", DockerCommandTimeout);
                return 0;
            }

            var imageIds = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var imageId in imageIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var removeProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = $"rmi {imageId}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (removeProcess != null)
                {
                    using var removeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    removeCts.CancelAfter(DockerCommandTimeout);

                    try
                    {
                        await removeProcess.WaitForExitAsync(removeCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (removeCts.IsCancellationRequested
                                                             && !cancellationToken.IsCancellationRequested)
                    {
                        KillProcessTree(removeProcess);
                        _logger.LogWarning("docker rmi timed out after {Timeout}", DockerCommandTimeout);
                    }
                }
            }

            return imageIds.Length;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean Docker images");
            return 0;
        }
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
        }
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
