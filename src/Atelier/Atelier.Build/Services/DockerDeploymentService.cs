using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Atelier.Build.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atelier.Build.Services;

public class DockerDeploymentService : IDeploymentService
{
    private readonly ILogger<DockerDeploymentService> _logger;
    private readonly string _registryDirectory;
    private readonly ConcurrentDictionary<string, DeploymentInfo> _deployments = new();

    private static readonly JsonSerializerOptions RegistryJsonOptions = new()
    {
        WriteIndented = true
    };

    public DockerDeploymentService(
        string? registryDirectory = null,
        ILogger<DockerDeploymentService>? logger = null)
    {
        _logger = logger ?? NullLogger<DockerDeploymentService>.Instance;
        _registryDirectory = registryDirectory
            ?? Path.Combine(Directory.GetCurrentDirectory(), ".atelier", "deployments");

        Reconcile();
    }

    private void Reconcile()
    {
        if (!Directory.Exists(_registryDirectory))
        {
            return;
        }

        foreach (var recordPath in Directory.GetFiles(_registryDirectory, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            try
            {
                var json = File.ReadAllText(recordPath);
                var record = JsonSerializer.Deserialize<DeploymentInfo>(json, RegistryJsonOptions);
                if (record is not null)
                {
                    _deployments[record.BoutiqueId] = record;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                _logger.LogWarning(ex, "Could not load deployment record {RecordPath}", recordPath);
            }
        }
    }

    private void PersistRecord(DeploymentInfo record)
    {
        Directory.CreateDirectory(_registryDirectory);
        var recordPath = Path.Combine(_registryDirectory, $"{record.BoutiqueId}.json");
        var json = JsonSerializer.Serialize(record, RegistryJsonOptions);
        var tempPath = $"{recordPath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, recordPath, overwrite: true);
    }

    private void RemoveRecord(string boutiqueId)
    {
        var recordPath = Path.Combine(_registryDirectory, $"{boutiqueId}.json");
        if (File.Exists(recordPath))
        {
            File.Delete(recordPath);
        }
    }

    public async Task<DeploymentResult> DeployDockerAsync(
        string boutiqueId,
        string artifactsPath,
        DockerDeploymentConfig configuration,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deploying boutique {BoutiqueId} via Docker", boutiqueId);

        try
        {
            var dockerComposePath = Path.Combine(artifactsPath, "docker-compose.yml");

            if (!File.Exists(dockerComposePath))
            {
                dockerComposePath = Path.Combine(
                    Path.GetDirectoryName(artifactsPath)!,
                    "..",
                    "docker-compose.yml");

                if (!File.Exists(dockerComposePath))
                {
                    return new DeploymentResult
                    {
                        Success = false,
                        ErrorMessage = "docker-compose.yml not found",
                        BoutiqueId = boutiqueId,
                        Target = DeploymentTarget.Docker
                    };
                }
            }

            var projectName = $"atelier-{boutiqueId}";
            var serviceName = configuration.ServiceName ?? boutiqueId;
            var composeDirectory = Path.GetDirectoryName(dockerComposePath)!;

            var arguments = new List<string>
            {
                "compose",
                "-f",
                dockerComposePath,
                "-p",
                projectName,
                "up",
                "-d"
            };

            foreach (var kv in configuration.EnvironmentVariables)
            {
                arguments.Add("-e");
                arguments.Add($"{kv.Key}={kv.Value}");
            }

            var (success, output) = await RunDockerCommandAsync(
                arguments,
                composeDirectory,
                cancellationToken).ConfigureAwait(false);

            if (!success)
            {
                await RunDockerCommandAsync(
                    new List<string> { "compose", "-f", dockerComposePath, "-p", projectName, "down" },
                    composeDirectory,
                    cancellationToken).ConfigureAwait(false);

                return new DeploymentResult
                {
                    Success = false,
                    ErrorMessage = output,
                    BoutiqueId = boutiqueId,
                    Target = DeploymentTarget.Docker
                };
            }

            var httpPort = await GetMappedPortAsync(projectName, serviceName, composeDirectory, Atelier.Build.Utils.DefaultPorts.Http, cancellationToken).ConfigureAwait(false);
            var grpcPort = await GetMappedPortAsync(projectName, serviceName, composeDirectory, Atelier.Build.Utils.DefaultPorts.Grpc, cancellationToken).ConfigureAwait(false);

            var deploymentInfo = new DeploymentInfo
            {
                BoutiqueId = boutiqueId,
                ProjectName = projectName,
                ServiceName = serviceName,
                DockerComposePath = dockerComposePath,
                State = DeploymentState.Running,
                HttpPort = httpPort,
                GrpcPort = grpcPort,
                DeployedAt = DateTime.UtcNow
            };

            _deployments[boutiqueId] = deploymentInfo;
            PersistRecord(deploymentInfo);

            _logger.LogInformation(
                "Boutique {BoutiqueId} deployed: HTTP={HttpPort}, gRPC={GrpcPort}",
                boutiqueId,
                httpPort,
                grpcPort);

            return new DeploymentResult
            {
                Success = true,
                BoutiqueId = boutiqueId,
                Target = DeploymentTarget.Docker,
                HttpEndpoint = httpPort.HasValue ? $"http://localhost:{httpPort}" : null,
                GrpcEndpoint = grpcPort.HasValue ? $"http://localhost:{grpcPort}" : null,
                Metadata = new()
                {
                    ["ProjectName"] = projectName,
                    ["DockerComposePath"] = dockerComposePath
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Docker deployment failed for {BoutiqueId}", boutiqueId);

            return new DeploymentResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                BoutiqueId = boutiqueId,
                Target = DeploymentTarget.Docker
            };
        }
    }

    public async Task<bool> StopAsync(string boutiqueId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping boutique {BoutiqueId}", boutiqueId);

        if (!_deployments.TryGetValue(boutiqueId, out var deployment))
        {
            _logger.LogWarning("Boutique {BoutiqueId} not found", boutiqueId);
            return false;
        }

        await DrainBeforeShutdownAsync(boutiqueId, cancellationToken).ConfigureAwait(false);

        var (success, _) = await RunDockerCommandAsync(
            new List<string> { "compose", "-p", deployment.ProjectName, "stop" },
            Path.GetDirectoryName(deployment.DockerComposePath)!,
            cancellationToken).ConfigureAwait(false);

        if (success)
        {
            deployment.State = DeploymentState.Stopped;
            PersistRecord(deployment);
        }

        return success;
    }

    public async Task<bool> StartAsync(string boutiqueId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting boutique {BoutiqueId}", boutiqueId);

        if (!_deployments.TryGetValue(boutiqueId, out var deployment))
        {
            _logger.LogWarning("Boutique {BoutiqueId} not found", boutiqueId);
            return false;
        }

        var (success, _) = await RunDockerCommandAsync(
            new List<string> { "compose", "-p", deployment.ProjectName, "start" },
            Path.GetDirectoryName(deployment.DockerComposePath)!,
            cancellationToken).ConfigureAwait(false);

        if (success)
        {
            deployment.State = DeploymentState.Running;
            PersistRecord(deployment);
        }

        return success;
    }

    public async Task<bool> TerminateAsync(
        string boutiqueId,
        bool removeVolumes = false,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Terminating boutique {BoutiqueId}", boutiqueId);

        if (!_deployments.TryGetValue(boutiqueId, out var deployment))
        {
            _logger.LogWarning("Boutique {BoutiqueId} not found", boutiqueId);
            return false;
        }

        await DrainBeforeShutdownAsync(boutiqueId, cancellationToken).ConfigureAwait(false);

        var downArgs = new List<string> { "compose", "-p", deployment.ProjectName, "down" };
        if (removeVolumes)
        {
            downArgs.Add("-v");
        }

        var (success, _) = await RunDockerCommandAsync(
            downArgs,
            Path.GetDirectoryName(deployment.DockerComposePath)!,
            cancellationToken).ConfigureAwait(false);

        if (success)
        {
            _deployments.TryRemove(boutiqueId, out _);
            RemoveRecord(boutiqueId);
        }

        return success;
    }

    public Task<DeploymentStatus> GetStatusAsync(
        string boutiqueId,
        CancellationToken cancellationToken = default)
    {

        if (!_deployments.TryGetValue(boutiqueId, out var deployment))
        {
            return Task.FromResult(new DeploymentStatus
            {
                BoutiqueId = boutiqueId,
                State = DeploymentState.Terminated,
                RunningInstances = 0,
                TargetInstances = 0,
                LastUpdated = DateTime.UtcNow
            });
        }

        return Task.FromResult(new DeploymentStatus
        {
            BoutiqueId = boutiqueId,
            State = deployment.State,
            RunningInstances = deployment.State == DeploymentState.Running ? 1 : 0,
            TargetInstances = 1,
            LastUpdated = DateTime.UtcNow
        });
    }

    private const string DRAIN_SECONDS_ENVIRONMENT_VARIABLE = "ATELIER_SHUTDOWN_DRAIN_SECONDS";
    private const int DEFAULT_DRAIN_SECONDS = 15;

    private async Task DrainBeforeShutdownAsync(string boutiqueId, CancellationToken cancellationToken)
    {
        var raw = Environment.GetEnvironmentVariable(DRAIN_SECONDS_ENVIRONMENT_VARIABLE);
        var seconds = int.TryParse(raw, out var parsed) && parsed >= 0
            ? parsed
            : DEFAULT_DRAIN_SECONDS;

        if (seconds == 0)
        {
            return;
        }

        _logger.LogInformation(
            "Draining boutique {BoutiqueId} for {DrainSeconds}s before shutdown",
            boutiqueId,
            seconds);

        await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken).ConfigureAwait(false);
    }

    private static readonly TimeSpan DockerCommandTimeout = TimeSpan.FromMinutes(5);

    private async Task<(bool Success, string Output)> RunDockerCommandAsync(
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "docker",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            processInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(processInfo);
        if (process == null)
        {
            return (false, "Failed to start docker process");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(DockerCommandTimeout);

        var outputTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested
                                                 && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception killEx) when (killEx is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
            }

            _logger.LogError("Docker command timed out after {Timeout}", DockerCommandTimeout);
            return (false, $"Docker command timed out after {DockerCommandTimeout}");
        }

        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);

        var combinedOutput = $"{output}\n{error}".Trim();

        if (process.ExitCode != 0)
        {
            _logger.LogError("Docker command failed: {Output}", RedactSensitiveValues(combinedOutput));
            return (false, combinedOutput);
        }

        return (true, combinedOutput);
    }

    private static readonly System.Text.RegularExpressions.Regex SensitiveAssignment =
        new(@"(?<key>[A-Za-z0-9_]*(?:PASSWORD|SECRET|TOKEN|KEY|CREDENTIAL)[A-Za-z0-9_]*)=(?<value>\S+)",
            System.Text.RegularExpressions.RegexOptions.Compiled
            | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static string RedactSensitiveValues(string text)
    {
        return SensitiveAssignment.Replace(text, m => $"{m.Groups["key"].Value}=***");
    }

    private async Task<int?> GetMappedPortAsync(
        string projectName,
        string serviceName,
        string composeDirectory,
        int containerPort,
        CancellationToken cancellationToken)
    {
        var (success, output) = await RunDockerCommandAsync(
            new List<string> { "compose", "-p", projectName, "port", serviceName, $"{containerPort}" },
            composeDirectory,
            cancellationToken).ConfigureAwait(false);

        if (!success || string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var parts = output.Trim().Split(':');
        if (parts.Length == 2 && int.TryParse(parts[1].Trim(), out var port))
        {
            return port;
        }

        return null;
    }

    private class DeploymentInfo
    {
        public string BoutiqueId { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public string ServiceName { get; set; } = string.Empty;
        public string DockerComposePath { get; set; } = string.Empty;
        public DeploymentState State { get; set; }
        public int? HttpPort { get; set; }
        public int? GrpcPort { get; set; }
        public DateTime DeployedAt { get; set; }
    }
}
