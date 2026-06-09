using System.Collections.Concurrent;
using Atelier.Build.Analysis;
using Atelier.Build.Discovery;
using Atelier.Build.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atelier.Build.Services;

public class AttacheService : IAttacheService
{
    private readonly IBoutiqueDiscoveryService _discoveryService;
    private readonly IDependencyAnalysisService _dependencyAnalysisService;
    private readonly IBoutiqueGenerationService _generationService;
    private readonly IBuildOrchestrationService _orchestrationService;
    private readonly IDeploymentService _deploymentService;
    private readonly ILogger<AttacheService> _logger;

    private readonly ConcurrentDictionary<string, BoutiqueInstance> _activeBoutiques = new();

    private const string DEFAULT_BOUTIQUE_VERSION = "1.0.0";
    private const string POSTGRES_CONNECTION_STRING_ENV = "ATELIER_DB";
    private const string REST_BASE_PATH = "/api";
    private const string HTTP_ENDPOINT_PROTOCOL = "http1-and-http2";
    private const string GRPC_ENDPOINT_PROTOCOL = "http2-only";
    private const string DOCKER_BASE_IMAGE = "mcr.microsoft.com/dotnet/aspnet:9.0-alpine";
    private const long BYTES_PER_GIB = 1024L * 1024L * 1024L;

    public AttacheService(
        IBoutiqueDiscoveryService discoveryService,
        IDependencyAnalysisService dependencyAnalysisService,
        IBoutiqueGenerationService generationService,
        IBuildOrchestrationService orchestrationService,
        IDeploymentService deploymentService,
        ILogger<AttacheService>? logger = null)
    {
        _discoveryService = discoveryService;
        _dependencyAnalysisService = dependencyAnalysisService;
        _generationService = generationService;
        _orchestrationService = orchestrationService;
        _deploymentService = deploymentService;
        _logger = logger ?? NullLogger<AttacheService>.Instance;
    }

        public async Task<BoutiqueInstance?> RequestBoutiqueAsync(
        BoutiqueRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Boutique requested: {Products}, {Capabilities}",
            string.Join(", ", request.RequiredProducts),
            request.Capabilities);

        try
        {
            var boutiqueId = GenerateBoutiqueId(request);

            if (_activeBoutiques.TryGetValue(boutiqueId, out var existingInstance))
            {
                _logger.LogInformation("Reusing existing boutique: {BoutiqueId}", boutiqueId);
                return existingInstance;
            }

            var solutionRoot = FindSolutionRoot();
            var compiledAssembliesDir = Path.Combine(solutionRoot, "src", "Atelier", "Atelier.Build", ".artifacts", "assemblies");

            var dependencyGraph = await _dependencyAnalysisService.AnalyzeProductDependenciesAsync(
                request.RequiredProducts.Select(p => (p, DeriveAssemblyName(p))),
                compiledAssembliesDir,
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Analyzed dependencies: {AssemblyCount} assemblies, {TypeCount} types",
                dependencyGraph.TotalAssemblyCount,
                dependencyGraph.TypeCount);

            var schema = ComposeYamlSchema(request, dependencyGraph);

            var boutiqueOutputDir = Path.Combine(solutionRoot, "boutiques", "dynamic", boutiqueId);
            Directory.CreateDirectory(boutiqueOutputDir);

            var artifacts = await _generationService.GenerateBoutiqueProjectAsync(
                schema,
                dependencyGraph,
                boutiqueOutputDir,
                solutionRoot,
                compiledAssembliesDir,
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Generated {Count} artifacts for boutique {BoutiqueId}",
                artifacts.AllPaths.Count,
                boutiqueId);

            var instance = new BoutiqueInstance
            {
                BoutiqueId = boutiqueId,
                Schema = schema,
                ArtifactsPath = boutiqueOutputDir,
                DependencyGraph = dependencyGraph,
                Status = BoutiqueStatus.Generated,
                CreatedAt = DateTime.UtcNow
            };

            _activeBoutiques[boutiqueId] = instance;

            _logger.LogInformation("Boutique artifacts generated: {BoutiqueId}", boutiqueId);

            if (request.AutoDeploy)
            {
                _logger.LogInformation("Auto-deploying boutique: {BoutiqueId}", boutiqueId);

                instance.Status = BoutiqueStatus.Deploying;

                var deployResult = await _deploymentService.DeployDockerAsync(
                    boutiqueId,
                    boutiqueOutputDir,
                    new DockerDeploymentConfig
                    {
                        ServiceName = schema.Name,
                        EnvironmentVariables = request.EnvironmentVariables ?? new()
                    },
                    cancellationToken).ConfigureAwait(false);

                if (deployResult.Success)
                {
                    instance.Status = BoutiqueStatus.Running;
                    instance.HttpEndpoint = deployResult.HttpEndpoint;
                    instance.GrpcEndpoint = deployResult.GrpcEndpoint;

                    _logger.LogInformation(
                        "Boutique deployed: {BoutiqueId} - HTTP={Http}, gRPC={Grpc}",
                        boutiqueId,
                        deployResult.HttpEndpoint,
                        deployResult.GrpcEndpoint);
                }
                else
                {
                    instance.Status = BoutiqueStatus.Failed;
                    _logger.LogError("Deployment failed: {Error}", deployResult.ErrorMessage);
                }
            }

            return instance;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create boutique: {Message}", ex.Message);
            return null;
        }
    }

    public async Task<bool> TerminateBoutiqueAsync(
        string boutiqueId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Terminating boutique: {BoutiqueId}", boutiqueId);

        if (!_activeBoutiques.TryGetValue(boutiqueId, out var instance))
        {
            _logger.LogWarning("Boutique not found: {BoutiqueId}", boutiqueId);
            return false;
        }

        if (instance.Status == BoutiqueStatus.Running || instance.Status == BoutiqueStatus.Stopped)
        {
            await _deploymentService.TerminateAsync(boutiqueId, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        instance.Status = BoutiqueStatus.Terminated;
        instance.TerminatedAt = DateTime.UtcNow;
        _activeBoutiques.TryRemove(boutiqueId, out _);

        return true;
    }

    public async Task<bool> StartBoutiqueAsync(
        string boutiqueId,
        CancellationToken cancellationToken = default)
    {
        if (!_activeBoutiques.TryGetValue(boutiqueId, out var instance))
        {
            return false;
        }

        if (instance.Status != BoutiqueStatus.Stopped)
        {
            return false;
        }

        var success = await _deploymentService.StartAsync(boutiqueId, cancellationToken).ConfigureAwait(false);

        if (success)
        {
            if (_activeBoutiques.TryGetValue(boutiqueId, out var running))
            {
                running.Status = BoutiqueStatus.Running;
            }
        }

        return success;
    }

    public async Task<bool> StopBoutiqueAsync(
        string boutiqueId,
        CancellationToken cancellationToken = default)
    {
        if (!_activeBoutiques.TryGetValue(boutiqueId, out var instance))
        {
            return false;
        }

        if (instance.Status != BoutiqueStatus.Running)
        {
            return false;
        }

        var success = await _deploymentService.StopAsync(boutiqueId, cancellationToken).ConfigureAwait(false);

        if (success)
        {
            if (_activeBoutiques.TryGetValue(boutiqueId, out var stopped))
            {
                stopped.Status = BoutiqueStatus.Stopped;
            }
        }

        return success;
    }

    public IReadOnlyList<BoutiqueInstance> ListBoutiques()
    {
        return _activeBoutiques.Values.ToList();
    }

    public BoutiqueInstance? GetBoutique(string boutiqueId)
    {
        return _activeBoutiques.TryGetValue(boutiqueId, out var instance) ? instance : null;
    }

    public BoutiqueMetrics? GetBoutiqueMetrics(string boutiqueId)
    {
        if (!_activeBoutiques.TryGetValue(boutiqueId, out var instance))
        {
            return null;
        }

        return new BoutiqueMetrics
        {
            BoutiqueId = boutiqueId,
            Status = instance.Status,
            UptimeSeconds = (DateTime.UtcNow - instance.CreatedAt).TotalSeconds,
            AssemblyCount = instance.DependencyGraph.TotalAssemblyCount,
            TypeCount = instance.DependencyGraph.TypeCount
        };
    }

    public async Task<BoutiqueHealthStatus> GetBoutiqueHealthAsync(
        string boutiqueId,
        CancellationToken cancellationToken = default)
    {
        var instance = GetBoutique(boutiqueId);

        if (instance == null)
        {
            return new BoutiqueHealthStatus
            {
                BoutiqueId = boutiqueId,
                State = HealthState.Unknown,
                Message = "Boutique not found",
                LastCheckAt = DateTime.UtcNow
            };
        }

        if (instance.Status != BoutiqueStatus.Running)
        {
            return new BoutiqueHealthStatus
            {
                BoutiqueId = boutiqueId,
                State = HealthState.Unhealthy,
                Message = $"Boutique is {instance.Status}",
                LastCheckAt = DateTime.UtcNow
            };
        }

        var deploymentStatus = await _deploymentService.GetStatusAsync(boutiqueId, cancellationToken).ConfigureAwait(false);

        var state = deploymentStatus.State == DeploymentState.Running
            ? HealthState.Healthy
            : HealthState.Unhealthy;

        return new BoutiqueHealthStatus
        {
            BoutiqueId = boutiqueId,
            State = state,
            Message = $"Deployment state: {deploymentStatus.State}",
            LastCheckAt = DateTime.UtcNow,
            Details = new()
            {
                ["RunningInstances"] = deploymentStatus.RunningInstances.ToString(),
                ["TargetInstances"] = deploymentStatus.TargetInstances.ToString()
            }
        };
    }

    private BoutiqueYamlSchema ComposeYamlSchema(BoutiqueRequest request, ProductDependencyGraph dependencyGraph)
    {
        var discoveredInfrastructure = DiscoverInfrastructureRequirements(
            dependencyGraph,
            request.InfrastructureOverrides);

        var schema = new BoutiqueYamlSchema
        {
            Name = $"dynamic-{GenerateBoutiqueId(request)}",
            Version = DEFAULT_BOUTIQUE_VERSION,
            Description = $"Dynamically composed boutique with products: {string.Join(", ", request.RequiredProducts)}",
            Products = request.RequiredProducts.Select(p => new ProductYaml
            {
                Type = p,
                Assembly = DeriveAssemblyName(p),
                AutoStart = true,
                Config = new Dictionary<string, object>()
            }).ToList(),
            Infrastructure = new InfrastructureYaml
            {
                Postgres = discoveredInfrastructure.RequiresPostgres
                    ? new PostgresYaml
                    {
                        Enabled = true,
                        ConnectionStringEnv = POSTGRES_CONNECTION_STRING_ENV
                    }
                    : null,
                Redis = discoveredInfrastructure.RequiresRedis
                    ? new RedisYaml
                    {
                        Enabled = true
                    }
                    : null,
                Hangfire = discoveredInfrastructure.RequiresHangfire
                    ? new HangfireYaml
                    {
                        Enabled = true
                    }
                    : null,
                SignalR = discoveredInfrastructure.RequiresSignalR
                    ? new SignalRYaml
                    {
                        Enabled = true
                    }
                    : null
            },
            Capabilities = new CapabilitiesYaml
            {
                Rest = new RestCapabilityYaml { Enabled = request.Capabilities.Rest, BasePath = REST_BASE_PATH },
                Grpc = new GrpcCapabilityYaml { Enabled = request.Capabilities.Grpc }
            },
            Kestrel = new KestrelYaml
            {
                Endpoints =
                [
                    new KestrelEndpointYaml
                    {
                        Name = "http",
                        Protocol = HTTP_ENDPOINT_PROTOCOL,
                        Port = request.RuntimeConfig.HttpPort ?? Atelier.Build.Utils.DefaultPorts.Http
                    },
                    new KestrelEndpointYaml
                    {
                        Name = "grpc",
                        Protocol = GRPC_ENDPOINT_PROTOCOL,
                        Port = request.RuntimeConfig.GrpcPort ?? Atelier.Build.Utils.DefaultPorts.Grpc
                    }
                ]
            },
            Resources = new ResourcesYaml
            {
                MaxMemoryBytes = request.RuntimeConfig.MaxMemoryGB * BYTES_PER_GIB,
                MaxCpuPercent = request.RuntimeConfig.MaxCpuPercent
            },
            Docker = new DockerYaml
            {
                BaseImage = DOCKER_BASE_IMAGE,
                Ports = [
                    request.RuntimeConfig.HttpPort ?? Atelier.Build.Utils.DefaultPorts.Http,
                    request.RuntimeConfig.GrpcPort ?? Atelier.Build.Utils.DefaultPorts.Grpc
                ]
            }
        };

        return schema;
    }

    private static string DeriveAssemblyName(string productType)
    {
        if (productType.Contains('.'))
        {
            var parts = productType.Split('.');
            return string.Join(".", parts.Take(parts.Length - 1));
        }

        return $"Atelier.Framework.{productType.Replace("Product", "")}";
    }

    private static string GenerateBoutiqueId(BoutiqueRequest request)
    {
        var productHash = string.Join("-", request.RequiredProducts.OrderBy(p => p, StringComparer.Ordinal));
        var hash = GetStableHashCode(productHash);
        return $"{hash:X8}";
    }

    private static int GetStableHashCode(string str)
    {
        unchecked
        {
            int hash1 = 5381;
            int hash2 = hash1;

            for (int i = 0; i < str.Length && str[i] != '\0'; i += 2)
            {
                hash1 = ((hash1 << 5) + hash1) ^ str[i];
                if (i == str.Length - 1 || str[i + 1] == '\0')
                {
                    break;
                }
                hash2 = ((hash2 << 5) + hash2) ^ str[i + 1];
            }

            return hash1 + (hash2 * 1566083941);
        }
    }

    private static string FindSolutionRoot()
    {
        var currentDir = Directory.GetCurrentDirectory();

        while (!string.IsNullOrEmpty(currentDir))
        {
            if (Directory.GetFiles(currentDir, "*.sln").Length > 0)
            {
                return currentDir;
            }

            currentDir = Path.GetDirectoryName(currentDir);
        }

        return Directory.GetCurrentDirectory();
    }

        private DiscoveredInfrastructure DiscoverInfrastructureRequirements(
        ProductDependencyGraph dependencyGraph,
        InfrastructureOverrides? overrides)
    {
        var infrastructure = new DiscoveredInfrastructure();

        var assemblyNames = dependencyGraph.GetAllAssemblies()
            .Select(a => a.ToLowerInvariant())
            .ToHashSet();

        if (overrides?.ForcePostgres.HasValue == true)
        {
            infrastructure.RequiresPostgres = overrides.ForcePostgres.Value;
        }
        else
        {
            infrastructure.RequiresPostgres =
                assemblyNames.Any(a =>
                    a.Contains("postgres") ||
                    a.Contains("entityframework") ||
                    a.Contains("knowledge") ||
                    a.Contains("memory"));
        }

        if (overrides?.ForceRedis.HasValue == true)
        {
            infrastructure.RequiresRedis = overrides.ForceRedis.Value;
        }
        else
        {
            infrastructure.RequiresRedis =
                assemblyNames.Any(a =>
                    a.Contains("redis") ||
                    a.Contains("cache") ||
                    a.Contains("vectornative") ||
                    a.Contains("streams"));
        }

        if (overrides?.ForceHangfire.HasValue == true)
        {
            infrastructure.RequiresHangfire = overrides.ForceHangfire.Value;
        }
        else
        {
            infrastructure.RequiresHangfire =
                assemblyNames.Any(a =>
                    a.Contains("hangfire") ||
                    a.Contains("backgroundjobs"));
        }

        if (overrides?.ForceSignalR.HasValue == true)
        {
            infrastructure.RequiresSignalR = overrides.ForceSignalR.Value;
        }
        else
        {
            infrastructure.RequiresSignalR =
                assemblyNames.Any(a =>
                    a.Contains("signalr") ||
                    a.Contains("realtime"));
        }

        return infrastructure;
    }

    private class DiscoveredInfrastructure
    {
        public bool RequiresPostgres { get; set; }
        public bool RequiresRedis { get; set; }
        public bool RequiresHangfire { get; set; }
        public bool RequiresSignalR { get; set; }
    }
}

public record BoutiqueRequest
{
        public required IReadOnlyList<string> RequiredProducts { get; init; }

        public required CapabilityRequirements Capabilities { get; init; }

        public required RuntimeConfig RuntimeConfig { get; init; }

        public bool AutoDeploy { get; init; } = true;

        public Dictionary<string, string>? EnvironmentVariables { get; init; }

        public InfrastructureOverrides? InfrastructureOverrides { get; init; }
}

public record CapabilityRequirements
{
    public bool Rest { get; init; }
    public bool Grpc { get; init; }
    public bool WebSocket { get; init; }
}

public record InfrastructureOverrides
{
    public bool? ForcePostgres { get; init; }
    public bool? ForceRedis { get; init; }
    public bool? ForceHangfire { get; init; }
    public bool? ForceSignalR { get; init; }
}

public record RuntimeConfig
{
    public int? HttpPort { get; init; }
    public int? GrpcPort { get; init; }
    public long MaxMemoryGB { get; init; } = 16;
    public int MaxCpuPercent { get; init; } = 80;
}

public class BoutiqueInstance
{
    public required string BoutiqueId { get; init; }
    public required BoutiqueYamlSchema Schema { get; init; }
    public required string ArtifactsPath { get; init; }
    public required ProductDependencyGraph DependencyGraph { get; init; }
    public BoutiqueStatus Status { get; set; }
    public DateTime CreatedAt { get; init; }
    public DateTime? TerminatedAt { get; set; }
    public string? HttpEndpoint { get; set; }
    public string? GrpcEndpoint { get; set; }
}

public enum BoutiqueStatus
{
    Generated,
    Building,
    Deploying,
    Running,
    Stopped,
    Failed,
    Terminated
}

public record BoutiqueMetrics
{
    public required string BoutiqueId { get; init; }
    public BoutiqueStatus Status { get; init; }
    public double UptimeSeconds { get; init; }
    public int AssemblyCount { get; init; }
    public int TypeCount { get; init; }
}
