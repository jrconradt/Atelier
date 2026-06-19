using Atelier.Build.Analysis;
using Atelier.Build.Discovery;
using Atelier.Build.Utils;

namespace Atelier.Build.Generation;

public static class BoutiqueResolver
{
    private const int DEFAULT_HEALTH_INTERVAL_SECONDS = 10;
    private const int DEFAULT_STARTUP_DELAY_SECONDS = 5;
    private const int DEFAULT_HEALTH_TIMEOUT_SECONDS = 5;
    private const int DEFAULT_HEALTH_RETRIES = 3;

    public static ResolvedBoutique Resolve(
        BoutiqueYamlSchema schema,
        ProductDependencyGraph dependencyGraph,
        string compiledAssembliesDir,
        bool verbose)
    {
        var ports = ResolvePorts(schema);
        var infraDeps = ResolveInfrastructureDependencies(schema);
        var health = ResolveHealth(schema, ports);

        return new ResolvedBoutique
        {
            Name = schema.Name,
            Version = schema.Version,
            Description = schema.Description,
            Ports = ports,
            Health = health,
            Environment = ResolveEnvironment(schema, ports, infraDeps),
            SecurityContext = ResolveSecurityContext(schema),
            ImageConfig = ResolveImageConfig(schema),
            Tls = ResolveTls(schema),
            NetworkZoning = ResolveNetworkZoning(dependencyGraph),
            InfrastructureDeps = infraDeps,
            ResourceLimits = ResolveResourceLimits(schema),
        };
    }

    private static ResolvedPortSet ResolvePorts(BoutiqueYamlSchema schema)
    {
        var endpoints = new List<ResolvedEndpoint>();

        if (schema.Kestrel?.Endpoints is not null)
        {
            foreach (var endpoint in schema.Kestrel.Endpoints)
            {
                endpoints.Add(new ResolvedEndpoint
                {
                    Name = endpoint.Name,
                    Port = endpoint.Port,
                    Protocol = endpoint.Protocol,
                    BindAddress = endpoint.BindAddress,
                    Tls = endpoint.Tls is null
                        ? null
                        : new ResolvedTlsEndpointConfig
                        {
                            EndpointName = endpoint.Name,
                            CertPath = endpoint.Tls.CertPath,
                            KeyPath = endpoint.Tls.KeyPath,
                            CertPathEnv = endpoint.Tls.CertPathEnv,
                            CertPasswordEnv = endpoint.Tls.CertPasswordEnv,
                        },
                });
            }
        }

        var httpPort = PortByName(endpoints, "http")
            ?? DockerPortAt(schema, 0)
            ?? DefaultPorts.Http;
        var grpcPort = PortByName(endpoints, "grpc")
            ?? DockerPortAt(schema, 1)
            ?? DefaultPorts.Grpc;
        var metricsPort = PortByName(endpoints, "metrics")
            ?? DockerPortAt(schema, 2)
            ?? DefaultPorts.Metrics;
        var gravityPort = PortByName(endpoints, "gravity")
            ?? PortByName(endpoints, "cluster");

        return new ResolvedPortSet
        {
            HttpPort = httpPort,
            GrpcPort = grpcPort,
            MetricsPort = metricsPort,
            GravityPort = gravityPort,
            AllEndpoints = endpoints,
        };
    }

    private static int? PortByName(List<ResolvedEndpoint> endpoints, string name)
    {
        foreach (var endpoint in endpoints)
        {
            if (endpoint.Name == name)
            {
                return endpoint.Port;
            }
        }

        return null;
    }

    private static int? DockerPortAt(BoutiqueYamlSchema schema, int index)
    {
        var ports = schema.Docker?.Ports;
        if (ports is null
            || ports.Count <= index)
        {
            return null;
        }

        return ports[index];
    }

    private static ResolvedHealthConfig ResolveHealth(BoutiqueYamlSchema schema, ResolvedPortSet ports)
    {
        var livenessPath = schema.Health?.Liveness?.Path ?? "/health";
        var readinessPath = schema.Health?.Readiness?.Path ?? "/ready";
        var interval = schema.Health?.Readiness?.IntervalSeconds ?? DEFAULT_HEALTH_INTERVAL_SECONDS;
        var startupDelay = schema.Health?.Readiness?.StartupDelaySeconds ?? DEFAULT_STARTUP_DELAY_SECONDS;
        var timeout = schema.Health?.Readiness is not null
            ? ResolveHealthTimeout(schema)
            : DEFAULT_HEALTH_TIMEOUT_SECONDS;
        var healthPort = schema.Docker?.HealthCheck?.Port
            ?? PortByName(ports.AllEndpoints.ToList(), "metrics")
            ?? PortByName(ports.AllEndpoints.ToList(), "http")
            ?? DefaultPorts.Http;

        return new ResolvedHealthConfig
        {
            LivenessPath = livenessPath,
            ReadinessPath = readinessPath,
            ReadinessIntervalSeconds = interval,
            ReadinessStartupDelaySeconds = startupDelay,
            TimeoutSeconds = timeout,
            Retries = DEFAULT_HEALTH_RETRIES,
            HealthcheckPort = healthPort,
        };
    }

    private static int ResolveHealthTimeout(BoutiqueYamlSchema schema)
    {
        var checks = schema.Health?.Checks;
        if (checks is not null)
        {
            foreach (var check in checks)
            {
                if (check.TimeoutSeconds.HasValue)
                {
                    return check.TimeoutSeconds.Value;
                }
            }
        }

        return DEFAULT_HEALTH_TIMEOUT_SECONDS;
    }

    private static ResolvedEnvironmentVariables ResolveEnvironment(
        BoutiqueYamlSchema schema,
        ResolvedPortSet ports,
        ResolvedInfrastructureDependencies infraDeps)
    {
        var aspnetEnvironment = "Production";
        var custom = new Dictionary<string, string>(StringComparer.Ordinal);

        if (schema.Docker?.Env is not null)
        {
            foreach (var (key, value) in schema.Docker.Env)
            {
                if (key == "ASPNETCORE_ENVIRONMENT")
                {
                    aspnetEnvironment = value;
                    continue;
                }

                if (key == "BOUTIQUE_MODE")
                {
                    continue;
                }

                custom[key] = value;
            }
        }

        var connectionStrings = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (infraDeps.PostgresEnabled
            && infraDeps.PostgresConnectionStringEnv is not null)
        {
            connectionStrings[infraDeps.PostgresConnectionStringEnv] = null;
        }
        if (infraDeps.RedisEnabled
            && infraDeps.RedisConnectionStringEnv is not null)
        {
            connectionStrings[infraDeps.RedisConnectionStringEnv] = null;
        }

        var all = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ASPNETCORE_ENVIRONMENT"] = aspnetEnvironment,
        };

        if (ports.GravityPort is not null)
        {
            all["GRAVITY_PORT"] = ports.GravityPort.Value.ToString();
        }

        foreach (var (key, value) in custom)
        {
            all[key] = value;
        }

        return new ResolvedEnvironmentVariables
        {
            AspNetCoreEnvironment = aspnetEnvironment,
            CustomVariables = custom,
            InfrastructureConnectionStrings = connectionStrings,
            AllVariables = all,
        };
    }

    private static ResolvedSecurityContext ResolveSecurityContext(BoutiqueYamlSchema schema)
    {
        var security = schema.Docker?.Security;

        if (security is null)
        {
            return new ResolvedSecurityContext();
        }

        return new ResolvedSecurityContext
        {
            Uid = security.Uid,
            Gid = security.Gid,
            Username = security.Username,
            GroupName = security.GroupName,
            ReadOnlyRootFilesystem = security.ReadOnlyRoot,
        };
    }

    private static ResolvedImageConfig ResolveImageConfig(BoutiqueYamlSchema schema)
    {
        var targetFramework = schema.Build?.TargetFramework ?? "net10.0";
        var sdkVersion = targetFramework switch
        {
            "net10.0" => "10.0",
            "net9.0"  => "9.0",
            "net8.0"  => "8.0",
            _         => throw new InvalidOperationException($"Unsupported target framework '{targetFramework}' for boutique '{schema.Name}'. Supported: net8.0, net9.0, net10.0.")
        };

        var defaultRuntimeImage = targetFramework switch
        {
            "net10.0" => "mcr.microsoft.com/dotnet/aspnet:10.0-alpine",
            "net9.0"  => "mcr.microsoft.com/dotnet/aspnet:9.0-alpine",
            "net8.0"  => "mcr.microsoft.com/dotnet/aspnet:8.0-alpine",
            _         => throw new InvalidOperationException($"Unsupported target framework '{targetFramework}' for boutique '{schema.Name}'. Supported: net8.0, net9.0, net10.0.")
        };

        var runtimeImage = schema.Docker?.BaseImage ?? defaultRuntimeImage;

        return new ResolvedImageConfig
        {
            TargetFramework = targetFramework,
            SdkImage = $"mcr.microsoft.com/dotnet/sdk:{sdkVersion}",
            RuntimeImage = runtimeImage,
            DockerTag = schema.Version,
            IsAlpine = runtimeImage.Contains("alpine"),
        };
    }

    private static ResolvedTlsConfig ResolveTls(BoutiqueYamlSchema schema)
    {
        var configs = new List<ResolvedTlsEndpointConfig>();

        if (schema.Kestrel?.Endpoints is not null)
        {
            foreach (var endpoint in schema.Kestrel.Endpoints)
            {
                if (endpoint.Tls is null)
                {
                    continue;
                }

                configs.Add(new ResolvedTlsEndpointConfig
                {
                    EndpointName = endpoint.Name,
                    CertPath = endpoint.Tls.CertPath,
                    KeyPath = endpoint.Tls.KeyPath,
                    CertPathEnv = endpoint.Tls.CertPathEnv,
                    CertPasswordEnv = endpoint.Tls.CertPasswordEnv,
                });
            }
        }

        return new ResolvedTlsConfig
        {
            EndpointConfigs = configs,
        };
    }

    private static ResolvedNetworkZoning ResolveNetworkZoning(ProductDependencyGraph dependencyGraph)
    {
        return new ResolvedNetworkZoning
        {
            IsolatedNetworks = dependencyGraph.IsolatedNetworks
                .OrderBy(network => network, StringComparer.Ordinal)
                .ToList(),
            ZonePolicies = dependencyGraph.ZonePolicies
                .OrderBy(policy => policy.Zone, StringComparer.Ordinal)
                .ToList(),
        };
    }

    private static ResolvedInfrastructureDependencies ResolveInfrastructureDependencies(BoutiqueYamlSchema schema)
    {
        var postgres = schema.Infrastructure?.Postgres;
        var redis = schema.Infrastructure?.Redis;

        return new ResolvedInfrastructureDependencies
        {
            PostgresEnabled = postgres?.Enabled == true,
            PostgresConnectionStringEnv = postgres?.Enabled == true ? postgres.ConnectionStringEnv : null,
            RedisEnabled = redis?.Enabled == true,
            RedisConnectionStringEnv = redis?.Enabled == true ? redis.ConnectionStringEnv : null,
            SignalREnabled = schema.Infrastructure?.SignalR?.Enabled == true,
            HangfireEnabled = schema.Infrastructure?.Hangfire?.Enabled == true,
        };
    }

    private static ResolvedResourceLimits ResolveResourceLimits(BoutiqueYamlSchema schema)
    {
        var dockerLimits = schema.Docker?.Resources?.Limits;
        var dockerReservations = schema.Docker?.Resources?.Reservations;

        var hasDockerLimits = dockerLimits is not null
            && (!string.IsNullOrEmpty(dockerLimits.Cpus)
                || !string.IsNullOrEmpty(dockerLimits.Memory));

        if (hasDockerLimits)
        {
            return new ResolvedResourceLimits
            {
                CpusLimit = string.IsNullOrEmpty(dockerLimits!.Cpus) ? null : dockerLimits.Cpus,
                MemoryLimit = string.IsNullOrEmpty(dockerLimits.Memory) ? null : dockerLimits.Memory,
                CpusReservation = string.IsNullOrEmpty(dockerReservations?.Cpus) ? null : dockerReservations.Cpus,
                MemoryReservation = string.IsNullOrEmpty(dockerReservations?.Memory) ? null : dockerReservations.Memory,
            };
        }

        var maxCpuPercent = schema.Resources?.MaxCpuPercent;
        var maxMemoryBytes = schema.Resources?.MaxMemoryBytes;

        if (!maxCpuPercent.HasValue
            && !maxMemoryBytes.HasValue)
        {
            return new ResolvedResourceLimits();
        }

        return new ResolvedResourceLimits
        {
            CpusLimit = maxCpuPercent.HasValue
                ? Math.Max(1, maxCpuPercent.Value / 25).ToString()
                : null,
            MemoryLimit = maxMemoryBytes.HasValue
                ? $"{maxMemoryBytes.Value / (1024 * 1024)}M"
                : null,
        };
    }
}
