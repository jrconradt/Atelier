namespace Atelier.Build.Services.Abstractions;

public interface IDeploymentService
{
        public Task<DeploymentResult> DeployDockerAsync(
        string boutiqueId,
        string artifactsPath,
        DockerDeploymentConfig configuration,
        CancellationToken cancellationToken = default);

        public Task<bool> StopAsync(
        string boutiqueId,
        CancellationToken cancellationToken = default);

        public Task<bool> StartAsync(
        string boutiqueId,
        CancellationToken cancellationToken = default);

        public Task<bool> TerminateAsync(
        string boutiqueId,
        bool removeVolumes = false,
        CancellationToken cancellationToken = default);

        public Task<DeploymentStatus> GetStatusAsync(
        string boutiqueId,
        CancellationToken cancellationToken = default);
}

public record DeploymentResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public required string BoutiqueId { get; init; }
    public required DeploymentTarget Target { get; init; }
    public string? HttpEndpoint { get; init; }
    public string? GrpcEndpoint { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new();
}

public record DeploymentStatus
{
    public required string BoutiqueId { get; init; }
    public DeploymentState State { get; init; }
    public int RunningInstances { get; init; }
    public int TargetInstances { get; init; }
    public DateTime LastUpdated { get; init; }
}

public enum DeploymentTarget
{
    Docker,
    Kubernetes,
    Process
}

public enum DeploymentState
{
    Deploying,
    Running,
    Stopped,
    Failed,
    Terminated
}

public record DockerDeploymentConfig
{
    public string? NetworkName { get; init; }
    public string? ServiceName { get; init; }
    public Dictionary<string, string> EnvironmentVariables { get; init; } = new();
    public List<string> Volumes { get; init; } = new();
}
