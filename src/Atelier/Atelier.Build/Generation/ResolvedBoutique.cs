using Atelier.Build.Analysis;

namespace Atelier.Build.Generation;

public class ResolvedBoutique
{
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string? Description { get; init; }

    public ResolvedPortSet Ports { get; init; } = new();
    public ResolvedHealthConfig Health { get; init; } = new();
    public ResolvedEnvironmentVariables Environment { get; init; } = new();
    public ResolvedSecurityContext SecurityContext { get; init; } = new();
    public ResolvedImageConfig ImageConfig { get; init; } = new();
    public ResolvedTlsConfig Tls { get; init; } = new();
    public ResolvedNetworkZoning NetworkZoning { get; init; } = new();
    public ResolvedInfrastructureDependencies InfrastructureDeps { get; init; } = new();
    public ResolvedResourceLimits ResourceLimits { get; init; } = new();
}

public class ResolvedPortSet
{
    public int HttpPort { get; init; }
    public int GrpcPort { get; init; }
    public int MetricsPort { get; init; }
    public int? GravityPort { get; init; }
    public IReadOnlyList<ResolvedEndpoint> AllEndpoints { get; init; } = [];
}

public class ResolvedEndpoint
{
    public string Name { get; init; } = string.Empty;
    public int Port { get; init; }
    public string Protocol { get; init; } = string.Empty;
    public string BindAddress { get; init; } = "0.0.0.0";
    public ResolvedTlsEndpointConfig? Tls { get; init; }
}

public class ResolvedHealthConfig
{
    public string LivenessPath { get; init; } = "/health";
    public string ReadinessPath { get; init; } = "/ready";
    public int ReadinessIntervalSeconds { get; init; } = 10;
    public int ReadinessStartupDelaySeconds { get; init; } = 5;
    public int TimeoutSeconds { get; init; } = 5;
    public int Retries { get; init; } = 3;
    public int HealthcheckPort { get; init; }
}

public class ResolvedEnvironmentVariables
{
    public string AspNetCoreEnvironment { get; init; } = "Production";
    public Dictionary<string, string> CustomVariables { get; init; } = [];
    public Dictionary<string, string?> InfrastructureConnectionStrings { get; init; } = [];
    public IReadOnlyDictionary<string, string> AllVariables { get; init; } = new Dictionary<string, string>();
}

public class ResolvedSecurityContext
{
    public int Uid { get; init; } = 64198;
    public int Gid { get; init; } = 64198;
    public string Username { get; init; } = "appuser";
    public string GroupName { get; init; } = "appuser";
    public bool ReadOnlyRootFilesystem { get; init; } = true;
    public bool DropAllCapabilities { get; init; } = true;
    public bool NoNewPrivileges { get; init; } = true;
}

public class ResolvedImageConfig
{
    public string TargetFramework { get; init; } = "net10.0";
    public string SdkImage { get; init; } = string.Empty;
    public string RuntimeImage { get; init; } = string.Empty;
    public string DockerTag { get; init; } = string.Empty;
    public bool IsAlpine { get; init; }
}

public class ResolvedTlsConfig
{
    public IReadOnlyList<ResolvedTlsEndpointConfig> EndpointConfigs { get; init; } = [];
}

public class ResolvedTlsEndpointConfig
{
    public string EndpointName { get; init; } = string.Empty;
    public string? CertPath { get; init; }
    public string? KeyPath { get; init; }
    public string? CertPathEnv { get; init; }
    public string? CertPasswordEnv { get; init; }
}

public class ResolvedNetworkZoning
{
    public IReadOnlyList<string> IsolatedNetworks { get; init; } = [];
    public IReadOnlyList<ZonePolicyInfo> ZonePolicies { get; init; } = [];
}

public class ResolvedInfrastructureDependencies
{
    public bool PostgresEnabled { get; init; }
    public string? PostgresConnectionStringEnv { get; init; }
    public bool RedisEnabled { get; init; }
    public string? RedisConnectionStringEnv { get; init; }
    public bool SignalREnabled { get; init; }
    public bool HangfireEnabled { get; init; }
}

public class ResolvedResourceLimits
{
    public string? CpusLimit { get; init; }
    public string? MemoryLimit { get; init; }
    public string? CpusReservation { get; init; }
    public string? MemoryReservation { get; init; }
}
