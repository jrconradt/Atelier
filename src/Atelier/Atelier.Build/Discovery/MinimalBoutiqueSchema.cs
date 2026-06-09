using YamlDotNet.Serialization;

namespace Atelier.Build.Discovery;

public class MinimalBoutiqueSchema
{
        [YamlMember(Alias = "name")]
    public string? Name { get; set; }

        [YamlMember(Alias = "ports")]
    public PortsSchema Ports { get; set; } = new();

        [YamlMember(Alias = "infrastructure")]
    public MinimalInfrastructureSchema? Infrastructure { get; set; }

        [YamlMember(Alias = "environment")]
    public Dictionary<string, string>? Environment { get; set; }

        [YamlMember(Alias = "volumes")]
    public List<string>? Volumes { get; set; }

        [YamlMember(Alias = "command")]
    public List<string>? Command { get; set; }

        [YamlMember(Alias = "healthcheck")]
    public HealthCheckSchema? Healthcheck { get; set; }

        [YamlMember(Alias = "products")]
    public List<ProductYaml>? Products { get; set; }

        [YamlMember(Alias = "capabilities")]
    public MinimalCapabilitiesSchema? Capabilities { get; set; }

        [YamlMember(Alias = "resources")]
    public MinimalResourcesSchema? Resources { get; set; }
}

public class MinimalCapabilitiesSchema
{
    [YamlMember(Alias = "rest")]
    public bool? Rest { get; set; }

    [YamlMember(Alias = "grpc")]
    public bool? Grpc { get; set; }

    [YamlMember(Alias = "websocket")]
    public bool? WebSocket { get; set; }
}

public class MinimalResourcesSchema
{
    [YamlMember(Alias = "max_memory_bytes")]
    public long? MaxMemoryBytes { get; set; }

    [YamlMember(Alias = "max_cpu_percent")]
    public int? MaxCpuPercent { get; set; }
}

public class PortsSchema
{
    [YamlMember(Alias = "http")]
    public int Http { get; set; } = Atelier.Build.Utils.DefaultPorts.Http;

    [YamlMember(Alias = "grpc")]
    public int Grpc { get; set; } = Atelier.Build.Utils.DefaultPorts.Grpc;

    [YamlMember(Alias = "metrics")]
    public int Metrics { get; set; } = Atelier.Build.Utils.DefaultPorts.Metrics;

        [YamlMember(Alias = "gravity")]
    public int? Gravity { get; set; }

    [YamlMember(Alias = "cluster")]
    public int? Cluster { get; set; }

        public int? GetGravityPort() => Cluster ?? Gravity;
}

public class MinimalInfrastructureSchema
{
    [YamlMember(Alias = "postgres")]
    public bool Postgres { get; set; }

    [YamlMember(Alias = "redis")]
    public bool Redis { get; set; }

    [YamlMember(Alias = "hangfire")]
    public bool Hangfire { get; set; }

    [YamlMember(Alias = "signalr")]
    public bool SignalR { get; set; }
}

public class HealthCheckSchema
{
    [YamlMember(Alias = "path")]
    public string Path { get; set; } = "/health";

    [YamlMember(Alias = "port")]
    public int? Port { get; set; }
}
