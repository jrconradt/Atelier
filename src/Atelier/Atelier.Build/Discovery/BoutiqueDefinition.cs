namespace Atelier.Build.Discovery;

public class BoutiqueDefinition
{
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string ProjectPath { get; init; }
    public string? Description { get; init; }

        public string? SourceDirectory { get; init; }

        public string? OutputDirectory { get; init; }

        public string? SubsystemName { get; init; }

        public IReadOnlyList<string> Dependencies { get; init; } = [];

        public IReadOnlyList<string> ProjectReferences { get; init; } = [];

        public IReadOnlyList<GrpcServiceDeclaration> GrpcServices { get; init; } = [];

        public PortConfiguration Ports { get; init; } = new();

        public InfrastructureConfiguration Infrastructure { get; init; } = new();

        public IReadOnlyList<OfferingDeclaration> Offerings { get; init; } = [];

        public IReadOnlyList<ProductYaml> Products { get; init; } = [];

    public BuildSettings Build { get; init; } = new();
    public DockerSettings? Docker { get; init; }

        public CapabilitiesConfiguration? Capabilities { get; init; }

        public ResourcesConfiguration? Resources { get; init; }
}

public class CapabilitiesConfiguration
{
    public bool? RestEnabled { get; init; }
    public bool? GrpcEnabled { get; init; }
    public bool? WebSocketEnabled { get; init; }
}

public class ResourcesConfiguration
{
    public long? MaxMemoryBytes { get; init; }
    public int? MaxCpuPercent { get; init; }
}

public class GrpcServiceDeclaration
{
    public required string ServiceName { get; init; }
    public required string Implementation { get; init; }
    public string? Assembly { get; init; }
}

public class PortConfiguration
{
    public int Http { get; init; } = Atelier.Build.Utils.DefaultPorts.Http;
    public int Grpc { get; init; } = Atelier.Build.Utils.DefaultPorts.Grpc;
    public int Metrics { get; init; } = Atelier.Build.Utils.DefaultPorts.Metrics;

        public int? Gravity { get; init; }
}

public class InfrastructureConfiguration
{
    public bool PostgresEnabled { get; init; }
    public bool RedisEnabled { get; init; }
    public bool HangfireEnabled { get; init; }
    public bool SignalREnabled { get; init; }
}

public class OfferingDeclaration
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public bool Enabled { get; init; } = true;
    public IReadOnlyDictionary<string, object> Configuration { get; init; } = new Dictionary<string, object>();
}

public class BuildSettings
{
    public string Configuration { get; init; } = "Release";
    public bool TreatWarningsAsErrors { get; init; } = true;
    public IReadOnlyList<string> AdditionalMsBuildArgs { get; init; } = [];
    public IReadOnlyList<string> Protos { get; init; } = [];
}

public class DockerSettings
{
    public string? BaseImage { get; init; }
    public IReadOnlyList<int> ExposedPorts { get; init; } = [];
    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<string> Volumes { get; init; } = [];
    public IReadOnlyList<string>? Command { get; init; }
    public HealthCheckConfiguration? HealthCheck { get; init; }
}

public class HealthCheckConfiguration
{
    public string Path { get; init; } = "/health";
    public int? Port { get; init; }
}
