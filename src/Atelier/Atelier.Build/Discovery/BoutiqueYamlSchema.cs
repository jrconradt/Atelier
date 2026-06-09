using YamlDotNet.Serialization;

namespace Atelier.Build.Discovery;

public class BoutiqueYamlSchema
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "version")]
    public string Version { get; set; } = "1.0.0";

    [YamlMember(Alias = "description")]
    public string? Description { get; set; }

        [YamlMember(Alias = "subsystem_name")]
    public string? SubsystemName { get; set; }

    [YamlMember(Alias = "dependencies")]
    public List<string>? Dependencies { get; set; }

    [YamlMember(Alias = "offerings")]
    public List<OfferingYaml>? Offerings { get; set; }

    [YamlMember(Alias = "products")]
    public List<ProductYaml>? Products { get; set; }

    [YamlMember(Alias = "infrastructure")]
    public InfrastructureYaml? Infrastructure { get; set; }

    [YamlMember(Alias = "capabilities")]
    public CapabilitiesYaml? Capabilities { get; set; }

    [YamlMember(Alias = "kestrel")]
    public KestrelYaml? Kestrel { get; set; }

    [YamlMember(Alias = "services")]
    public List<ServiceRegistrationYaml>? Services { get; set; }

    [YamlMember(Alias = "resources")]
    public ResourcesYaml? Resources { get; set; }

    [YamlMember(Alias = "health")]
    public HealthYaml? Health { get; set; }

    [YamlMember(Alias = "build")]
    public BuildYaml? Build { get; set; }

    [YamlMember(Alias = "docker")]
    public DockerYaml? Docker { get; set; }

    [YamlMember(Alias = "static_files")]
    public StaticFilesYaml? StaticFiles { get; set; }

    [YamlMember(Alias = "grpc_services")]
    public List<GrpcServiceYaml>? GrpcServices { get; set; }

    [YamlMember(Alias = "project_references")]
    public List<string>? ProjectReferences { get; set; }

    [YamlMember(Alias = "package_references")]
    public Dictionary<string, string>? PackageReferences { get; set; }
}

public class ProductYaml
{
    [YamlMember(Alias = "type")]
    public string Type { get; set; } = string.Empty;

    [YamlMember(Alias = "assembly")]
    public string? Assembly { get; set; }

    [YamlMember(Alias = "auto_start")]
    public bool AutoStart { get; set; } = true;

    [YamlMember(Alias = "config")]
    public Dictionary<string, object>? Config { get; set; }
}

public class InfrastructureYaml
{
    [YamlMember(Alias = "postgres")]
    public PostgresYaml? Postgres { get; set; }

    [YamlMember(Alias = "redis")]
    public RedisYaml? Redis { get; set; }

    [YamlMember(Alias = "hangfire")]
    public HangfireYaml? Hangfire { get; set; }

    [YamlMember(Alias = "signalr")]
    public SignalRYaml? SignalR { get; set; }

    [YamlMember(Alias = "network")]
    public NetworkYaml? Network { get; set; }
}

public class PostgresYaml
{
    [YamlMember(Alias = "enabled")]
    public bool Enabled { get; set; } = true;

    [YamlMember(Alias = "connection_string_env")]
    public string ConnectionStringEnv { get; set; } = "ATELIER_VECTORNATIVE_DB";
}

public class RedisYaml
{
    [YamlMember(Alias = "enabled")]
    public bool Enabled { get; set; } = true;

    [YamlMember(Alias = "connection_string_env")]
    public string ConnectionStringEnv { get; set; } = "ATELIER_REDIS_CONNECTION";
}

public class HangfireYaml
{
    [YamlMember(Alias = "enabled")]
    public bool Enabled { get; set; }
}

public class SignalRYaml
{
    [YamlMember(Alias = "enabled")]
    public bool Enabled { get; set; }
}

public class NetworkYaml
{
    [YamlMember(Alias = "enabled")]
    public bool Enabled { get; set; }

    [YamlMember(Alias = "isolation_level")]
    public string IsolationLevel { get; set; } = "Standard";

    [YamlMember(Alias = "require_mutual_tls")]
    public bool RequireMutualTls { get; set; }

    [YamlMember(Alias = "default_network_name")]
    public string DefaultNetworkName { get; set; } = "atelier-network";

    [YamlMember(Alias = "network_driver")]
    public string NetworkDriver { get; set; } = "bridge";

    [YamlMember(Alias = "default_zone")]
    public string? DefaultZone { get; set; }

    [YamlMember(Alias = "zones")]
    public List<NetworkZoneYaml>? Zones { get; set; }

    [YamlMember(Alias = "policies")]
    public List<NetworkPolicyYaml>? Policies { get; set; }
}

public class NetworkZoneYaml
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "type")]
    public string Type { get; set; } = string.Empty;

    [YamlMember(Alias = "isolation_level")]
    public string? IsolationLevel { get; set; }

    [YamlMember(Alias = "isolated")]
    public bool Isolated { get; set; }

    [YamlMember(Alias = "require_mutual_tls")]
    public bool? RequireMutualTls { get; set; }

    [YamlMember(Alias = "allowed_inbound_zones")]
    public List<string>? AllowedInboundZones { get; set; }

    [YamlMember(Alias = "allowed_outbound_zones")]
    public List<string>? AllowedOutboundZones { get; set; }
}

public class NetworkPolicyYaml
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "source_zone")]
    public string SourceZone { get; set; } = string.Empty;

    [YamlMember(Alias = "target_zone")]
    public string TargetZone { get; set; } = string.Empty;

    [YamlMember(Alias = "allowed")]
    public bool Allowed { get; set; } = true;

    [YamlMember(Alias = "require_mutual_tls")]
    public bool? RequireMutualTls { get; set; }
}

public class CapabilitiesYaml
{
    [YamlMember(Alias = "rest")]
    public RestCapabilityYaml? Rest { get; set; }

    [YamlMember(Alias = "grpc")]
    public GrpcCapabilityYaml? Grpc { get; set; }

    [YamlMember(Alias = "websocket")]
    public WebSocketCapabilityYaml? WebSocket { get; set; }

    [YamlMember(Alias = "graphql")]
    public GraphQLCapabilityYaml? GraphQL { get; set; }
}

public class RestCapabilityYaml
{
    [YamlMember(Alias = "enabled")]
    public bool Enabled { get; set; } = true;

    [YamlMember(Alias = "base_path")]
    public string BasePath { get; set; } = "/api";
}

public class GrpcCapabilityYaml
{
    [YamlMember(Alias = "enabled")]
    public bool Enabled { get; set; }

    [YamlMember(Alias = "max_receive_message_size")]
    public int? MaxReceiveMessageSize { get; set; }

    [YamlMember(Alias = "max_send_message_size")]
    public int? MaxSendMessageSize { get; set; }
}

public class WebSocketCapabilityYaml
{
    [YamlMember(Alias = "enabled")]
    public bool Enabled { get; set; }
}

public class StaticFilesYaml
{
    [YamlMember(Alias = "enabled")]
    public bool Enabled { get; set; } = true;

    [YamlMember(Alias = "root_path")]
    public string RootPath { get; set; } = "wwwroot";

    [YamlMember(Alias = "default_file")]
    public string? DefaultFile { get; set; }
}

public class GrpcServiceYaml
{
    [YamlMember(Alias = "implementation")]
    public string Implementation { get; set; } = string.Empty;

    [YamlMember(Alias = "assembly")]
    public string? Assembly { get; set; }
}

public class GraphQLCapabilityYaml
{
    [YamlMember(Alias = "enabled")]
    public bool Enabled { get; set; }
}

public class KestrelYaml
{
    [YamlMember(Alias = "endpoints")]
    public List<KestrelEndpointYaml>? Endpoints { get; set; }
}

public class KestrelEndpointYaml
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "protocol")]
    public string Protocol { get; set; } = "http1-and-http2";

    [YamlMember(Alias = "port")]
    public int Port { get; set; }

    [YamlMember(Alias = "bind_address")]
    public string BindAddress { get; set; } = "0.0.0.0";

    [YamlMember(Alias = "path_base")]
    public string? PathBase { get; set; }

    [YamlMember(Alias = "tls")]
    public TlsYaml? Tls { get; set; }
}

public class TlsYaml
{
    [YamlMember(Alias = "cert_path")]
    public string? CertPath { get; set; }

    [YamlMember(Alias = "key_path")]
    public string? KeyPath { get; set; }

    [YamlMember(Alias = "cert_path_env")]
    public string? CertPathEnv { get; set; }

    [YamlMember(Alias = "cert_password_env")]
    public string? CertPasswordEnv { get; set; }
}

public class ServiceRegistrationYaml
{
    [YamlMember(Alias = "interface")]
    public string? Interface { get; set; }

    [YamlMember(Alias = "implementation")]
    public string Implementation { get; set; } = string.Empty;

    [YamlMember(Alias = "lifetime")]
    public string Lifetime { get; set; } = "Scoped";

    [YamlMember(Alias = "assembly")]
    public string? Assembly { get; set; }

    [YamlMember(Alias = "hosted_service")]
    public bool HostedService { get; set; } = false;
}

public class ResourcesYaml
{
    [YamlMember(Alias = "max_memory_bytes")]
    public long? MaxMemoryBytes { get; set; }

    [YamlMember(Alias = "max_cpu_percent")]
    public int? MaxCpuPercent { get; set; }
}

public class HealthYaml
{
    [YamlMember(Alias = "liveness")]
    public HealthEndpointYaml? Liveness { get; set; }

    [YamlMember(Alias = "readiness")]
    public HealthEndpointYaml? Readiness { get; set; }

    [YamlMember(Alias = "checks")]
    public List<HealthCheckYaml>? Checks { get; set; }

    [YamlMember(Alias = "timeout_seconds")]
    public int TimeoutSeconds { get; set; } = 5;

    [YamlMember(Alias = "retries")]
    public int Retries { get; set; } = 3;
}

public class HealthEndpointYaml
{
    [YamlMember(Alias = "path")]
    public string Path { get; set; } = "/health";

    [YamlMember(Alias = "interval_seconds")]
    public int IntervalSeconds { get; set; } = 10;

    [YamlMember(Alias = "startup_delay_seconds")]
    public int? StartupDelaySeconds { get; set; }
}

public class HealthCheckYaml
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "type")]
    public string Type { get; set; } = "builtin";

    [YamlMember(Alias = "connection_string_env")]
    public string? ConnectionStringEnv { get; set; }

    [YamlMember(Alias = "timeout_seconds")]
    public int? TimeoutSeconds { get; set; }
}

public class OfferingYaml
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "description")]
    public string? Description { get; set; }

    [YamlMember(Alias = "enabled")]
    public bool Enabled { get; set; } = true;

    [YamlMember(Alias = "config")]
    public Dictionary<string, object>? Config { get; set; }
}

public class BuildYaml
{
    [YamlMember(Alias = "configuration")]
    public string Configuration { get; set; } = "Release";

    [YamlMember(Alias = "target_framework")]
    public string TargetFramework { get; set; } = "net10.0";

    [YamlMember(Alias = "treat_warnings_as_errors")]
    public bool TreatWarningsAsErrors { get; set; } = true;

    [YamlMember(Alias = "allow_unsafe_blocks")]
    public bool AllowUnsafeBlocks { get; set; } = false;

    [YamlMember(Alias = "protos")]
    public List<string>? Protos { get; set; }

    [YamlMember(Alias = "msbuild_args")]
    public List<string>? MsBuildArgs { get; set; }
}

public class DockerYaml
{
    [YamlMember(Alias = "base_image")]
    public string? BaseImage { get; set; }

    [YamlMember(Alias = "ports")]
    public List<int>? Ports { get; set; }

    [YamlMember(Alias = "port_mappings")]
    public Dictionary<int, int>? PortMappings { get; set; }

    [YamlMember(Alias = "env")]
    public Dictionary<string, string>? Env { get; set; }

    [YamlMember(Alias = "volumes")]
    public List<string>? Volumes { get; set; }

    [YamlMember(Alias = "command")]
    public List<string>? Command { get; set; }

    [YamlMember(Alias = "healthcheck")]
    public DockerHealthCheckYaml? HealthCheck { get; set; }

    [YamlMember(Alias = "resources")]
    public DockerResourcesYaml? Resources { get; set; }

    [YamlMember(Alias = "restart_policy")]
    public string RestartPolicy { get; set; } = "unless-stopped";

    [YamlMember(Alias = "tmpfs")]
    public List<string>? Tmpfs { get; set; }

    [YamlMember(Alias = "labels")]
    public Dictionary<string, string>? Labels { get; set; }

    [YamlMember(Alias = "security")]
    public DockerSecurityYaml? Security { get; set; }
}

public class DockerSecurityYaml
{
    [YamlMember(Alias = "read_only_root")]
    public bool ReadOnlyRoot { get; set; } = true;

    [YamlMember(Alias = "uid")]
    public int Uid { get; set; } = 64198;

    [YamlMember(Alias = "gid")]
    public int Gid { get; set; } = 64198;

    [YamlMember(Alias = "username")]
    public string Username { get; set; } = "appuser";

    [YamlMember(Alias = "group_name")]
    public string GroupName { get; set; } = "appuser";
}

public class DockerHealthCheckYaml
{
    [YamlMember(Alias = "path")]
    public string Path { get; set; } = "/health";

    [YamlMember(Alias = "port")]
    public int? Port { get; set; }
}

public class DockerResourcesYaml
{
    [YamlMember(Alias = "limits")]
    public DockerResourceLimitsYaml? Limits { get; set; }

    [YamlMember(Alias = "reservations")]
    public DockerResourceLimitsYaml? Reservations { get; set; }
}

public class DockerResourceLimitsYaml
{
    [YamlMember(Alias = "cpus")]
    public string? Cpus { get; set; }

    [YamlMember(Alias = "memory")]
    public string? Memory { get; set; }
}
