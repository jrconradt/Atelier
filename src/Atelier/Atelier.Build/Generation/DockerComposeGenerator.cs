using Atelier.Build.Discovery;
using Atelier.Build.Pipeline;
using Spectre.Console;
using Templar.Rendering;
using T = Atelier.Build.Templates.Docker;

namespace Atelier.Build.Generation;

public class DockerComposeGenerator
{
    public const string DEFAULT_NETWORK_NAME = "atelier-network";

    private readonly BuildContext _context;

    public DockerComposeGenerator(BuildContext context)
    {
        _context = context;
    }

    private static string SanitizeScalar(string? value)
    {
        return Atelier.Build.Utils.GeneratorText.SanitizeScalar(value);
    }

    private static string EscapeQuotedScalar(string? value)
    {
        return Atelier.Build.Utils.GeneratorText.EscapeQuotedScalar(value);
    }

    private static string SanitizeHealthPath(string? value)
    {
        return Atelier.Build.Utils.GeneratorText.SanitizeHealthPath(value);
    }

    private static readonly HashSet<string> AllowedVolumeModes = new(StringComparer.Ordinal)
    {
        "ro",
        "rw",
        "z",
        "Z",
        "ro,z",
        "rw,z",
        "ro,Z",
        "rw,Z"
    };

    private static bool IsDangerousVolumeSpec(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec)
            || spec.Contains('\r')
            || spec.Contains('\n')
            || spec.Contains('$')
            || spec.Contains("docker.sock")
            || spec.Contains(".."))
        {
            return true;
        }

        var parts = spec.Split(':');
        if (parts.Length < 2
            || parts.Length > 3)
        {
            return true;
        }

        var hostSide = parts[0];
        if (hostSide.Length == 0
            || Path.IsPathRooted(hostSide))
        {
            return true;
        }

        var containerSide = parts[1];
        if (containerSide.Length == 0
            || !Path.IsPathRooted(containerSide))
        {
            return true;
        }

        if (parts.Length == 3
            && !AllowedVolumeModes.Contains(parts[2]))
        {
            return true;
        }

        return false;
    }

    public async Task<string> GenerateAsync(
        List<BoutiqueYamlSchema> boutiques,
        List<ResolvedBoutique> resolved,
        string outputPath,
        string relativePathToRoot = ".")
    {
        var byName = resolved.ToDictionary(r => r.Name, StringComparer.Ordinal);

        var services = Sequence.Lines(boutiques.OrderBy(b => b.Name, StringComparer.Ordinal)
            .Select(b => (Compositor)RenderBoutiqueService(b, byName[b.Name], relativePathToRoot)));

        var content = new T.Compose
        {
            Header = new T.ComposeHeader(),
            Infrastructure = RenderInfrastructure(resolved),
            Services = services,
            NetworksAndVolumes = new T.NetworksAndVolumes
            {
                NetworkLines = RenderTopLevelNetworks(resolved),
                VolumesBlock = RenderTopLevelVolumes(resolved),
            },
        }.Render();

        await File.WriteAllTextAsync(outputPath, content).ConfigureAwait(false);

        if (_context.Verbose)
        {
            AnsiConsole.MarkupLine($"[dim]    → Generated docker-compose.yml ({content.Split('\n').Length} lines)[/]");
        }

        return outputPath;
    }

    private static Sequence RenderTopLevelNetworks(List<ResolvedBoutique> resolved)
    {
        var networks = new List<Compositor>
        {
            new T.NetworkEntry
            {
                Name = DEFAULT_NETWORK_NAME,
                Driver = "bridge",
            },
        };

        var isolated = resolved
            .SelectMany(r => r.NetworkZoning.IsolatedNetworks)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal);

        foreach (var network in isolated)
        {
            networks.Add(new T.IsolatedNetworkEntry
            {
                Name = SanitizeScalar(network),
                Driver = "bridge",
            });
        }

        var microSegmented = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in resolved)
        {
            var shortName = r.Name.Replace("atelier-", string.Empty).ToLowerInvariant();
            if (r.InfrastructureDeps.RedisEnabled)
            {
                microSegmented.Add($"net-{shortName}-redis");
            }
            if (r.InfrastructureDeps.PostgresEnabled)
            {
                microSegmented.Add($"net-{shortName}-postgres");
            }
        }

        foreach (var network in microSegmented.OrderBy(n => n, StringComparer.Ordinal))
        {
            networks.Add(new T.IsolatedNetworkEntry
            {
                Name = SanitizeScalar(network),
                Driver = "bridge",
            });
        }

        return Sequence.Lines(networks);
    }

    private static Compositor RenderTopLevelVolumes(List<ResolvedBoutique> resolved)
    {
        var needsPostgres = resolved.Any(r => r.InfrastructureDeps.PostgresEnabled);
        var needsRedis    = resolved.Any(r => r.InfrastructureDeps.RedisEnabled);

        var names = new List<string>();
        if (needsPostgres)
        {
            names.Add(InfrastructurePolicy.PostgresVolume);
        }
        if (needsRedis)
        {
            names.Add(InfrastructurePolicy.RedisVolume);
        }

        if (names.Count == 0)
        {
            return new T.InfrastructureEmpty();
        }

        var volumeLines = Sequence.Lines(names.OrderBy(n => n, StringComparer.Ordinal)
            .Select(n => (Compositor)new T.TopLevelVolumeName { Name = n }));

        return new T.TopLevelVolumes { VolumeNames = volumeLines };
    }

    private static Compositor RenderInfrastructure(List<ResolvedBoutique> resolved)
    {
        var needsPostgres = resolved.Any(r => r.InfrastructureDeps.PostgresEnabled);
        var needsRedis    = resolved.Any(r => r.InfrastructureDeps.RedisEnabled);

        if (!needsPostgres && !needsRedis)
        {
            return new T.InfrastructureEmpty();
        }

        var sections = new List<Compositor>();
        if (needsPostgres)
        {
            var pgNetworks = new List<string> { DEFAULT_NETWORK_NAME };
            foreach (var r in resolved)
            {
                if (r.InfrastructureDeps.PostgresEnabled)
                {
                    var shortName = r.Name.Replace("atelier-", string.Empty).ToLowerInvariant();
                    pgNetworks.Add($"net-{shortName}-postgres");
                }
            }

            var pgNetworksSeq = Sequence.Lines(pgNetworks.Distinct(StringComparer.Ordinal)
                .Select(n => (Compositor)new T.ServiceNetworkLine { Name = SanitizeScalar(n) }));

            sections.Add(new T.Postgres
            {
                Image = InfrastructurePolicy.PostgresImage,
                User = InfrastructurePolicy.PostgresUser,
                Database = InfrastructurePolicy.PostgresDatabase,
                Port = InfrastructurePolicy.PostgresPort,
                Volume = InfrastructurePolicy.PostgresVolume,
                Networks = pgNetworksSeq,
            });
        }
        if (needsRedis)
        {
            var redisNetworks = new List<string> { DEFAULT_NETWORK_NAME };
            foreach (var r in resolved)
            {
                if (r.InfrastructureDeps.RedisEnabled)
                {
                    var shortName = r.Name.Replace("atelier-", string.Empty).ToLowerInvariant();
                    redisNetworks.Add($"net-{shortName}-redis");
                }
            }

            var redisNetworksSeq = Sequence.Lines(redisNetworks.Distinct(StringComparer.Ordinal)
                .Select(n => (Compositor)new T.ServiceNetworkLine { Name = SanitizeScalar(n) }));

            sections.Add(new T.Redis
            {
                Image = InfrastructurePolicy.RedisImage,
                Port = InfrastructurePolicy.RedisPort,
                Volume = InfrastructurePolicy.RedisVolume,
                Networks = redisNetworksSeq,
            });
        }

        return new T.InfrastructureHeader
        {
            Sections = Sequence.Lines(sections),
        };
    }

    private static Compositor RenderBoutiqueService(
        BoutiqueYamlSchema boutique,
        ResolvedBoutique resolved,
        string relativePathToRoot)
    {
        var shortName = boutique.Name.Replace("atelier-", string.Empty).ToLowerInvariant();

        var sections = new List<Compositor>();
        var command = RenderCommand(boutique);
        if (command is not null)
        {
            sections.Add(command);
        }
        var dependsOn = RenderDependsOn(resolved);
        if (dependsOn is not null)
        {
            sections.Add(dependsOn);
        }
        sections.Add(RenderEnvironmentVariables(resolved));
        var ports = RenderPorts(boutique, resolved);
        if (ports is not null)
        {
            sections.Add(ports);
        }
        sections.Add(RenderVolumes(boutique, relativePathToRoot));
        sections.Add(RenderHealthCheck(resolved));
        var resources = RenderResources(resolved);
        if (resources is not null)
        {
            sections.Add(resources);
        }

        return new T.BoutiqueService
        {
            BoutiqueLabel = EscapeQuotedScalar(boutique.Name.ToUpperInvariant()),
            Description = EscapeQuotedScalar(boutique.Description),
            BoutiqueName = SanitizeScalar(boutique.Name),
            Context = relativePathToRoot,
            ShortName = SanitizeScalar(shortName),
            ImageName = SanitizeScalar(boutique.Name.ToLowerInvariant()),
            ContainerName = SanitizeScalar(boutique.Name.ToLowerInvariant()),
            ImmutableTag = SanitizeDockerTag(boutique.Version),
            Networks = RenderServiceNetworks(resolved),
            Sections = Sequence.Lines(sections),
        };
    }

    private static Sequence RenderServiceNetworks(ResolvedBoutique resolved)
    {
        var names = new List<string> { DEFAULT_NETWORK_NAME };
        names.AddRange(resolved.NetworkZoning.IsolatedNetworks);

        var shortName = resolved.Name.Replace("atelier-", string.Empty).ToLowerInvariant();
        if (resolved.InfrastructureDeps.RedisEnabled)
        {
            names.Add($"net-{shortName}-redis");
        }
        if (resolved.InfrastructureDeps.PostgresEnabled)
        {
            names.Add($"net-{shortName}-postgres");
        }

        return Sequence.Lines(names.Distinct(StringComparer.Ordinal)
            .Select(n => (Compositor)new T.ServiceNetworkLine { Name = SanitizeScalar(n) }));
    }

    private static string SanitizeDockerTag(string? version)
    {
        var sanitized = SanitizeScalar(version);
        if (string.IsNullOrEmpty(sanitized))
        {
            return "0.0.0";
        }

        var allowed = new List<char>();
        foreach (var ch in sanitized)
        {
            if (char.IsLetterOrDigit(ch)
                || ch == '.'
                || ch == '_'
                || ch == '-')
            {
                allowed.Add(ch);
            }
        }

        if (allowed.Count == 0
            || !char.IsLetterOrDigit(allowed[0]))
        {
            return "0.0.0";
        }

        var tag = new string(allowed.ToArray());
        return tag.Length > 128 ? tag.Substring(0, 128) : tag;
    }

    private static Compositor? RenderDependsOn(ResolvedBoutique resolved)
    {
        var services = new List<string>();
        if (resolved.InfrastructureDeps.PostgresEnabled)
        {
            services.Add("postgres");
        }
        if (resolved.InfrastructureDeps.RedisEnabled)
        {
            services.Add("redis");
        }

        if (services.Count == 0)
        {
            return null;
        }

        var dependencyLines = Sequence.Lines(services.OrderBy(s => s, StringComparer.Ordinal)
            .Select(s => (Compositor)new T.DependsOnLine { Service = s }));

        return new T.DependsOn { DependencyLines = dependencyLines };
    }

    private static Compositor? RenderCommand(BoutiqueYamlSchema b)
    {
        if (b.Docker?.Command is null || b.Docker.Command.Count == 0)
        {
            return null;
        }

        var args = Sequence.CommaList(b.Docker.Command.Select(a => (Compositor)new T.CommandArg { Arg = SanitizeScalar(a) }));

        return new T.Command { Args = args };
    }

    private static Compositor RenderEnvironmentVariables(ResolvedBoutique resolved)
    {
        var envVars = new Dictionary<string, string>(resolved.Environment.AllVariables, StringComparer.Ordinal);

        foreach (var (key, value) in resolved.Environment.InfrastructureConnectionStrings)
        {
            envVars[key] = value ?? $"${{{key}}}";
        }

        var envLines = Sequence.Lines(envVars.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => (Compositor)new T.EnvVarLine
                {
                    Key = EscapeQuotedScalar(kv.Key),
                    Value = EscapeQuotedScalar(kv.Value),
                }));

        return new T.Environment { EnvLines = envLines };
    }

    private static Compositor? RenderPorts(BoutiqueYamlSchema b, ResolvedBoutique resolved)
    {
        if (b.Docker?.Ports is null || b.Docker.Ports.Count == 0)
        {
            return null;
        }

        var portMappings = b.Docker.PortMappings ?? new Dictionary<int, int>();
        var gravityPort = resolved.Ports.GravityPort;

        var portLines = Sequence.Lines(b.Docker.Ports.OrderBy(p => p).Select(internalPort =>
            {
                var externalPort = portMappings.TryGetValue(internalPort, out var mapped) ? mapped : internalPort;
                return (Compositor)new T.PortLine
                {
                    External = externalPort,
                    Internal = internalPort,
                    UdpSuffix = EndpointResolution.UdpSuffixFor(internalPort, gravityPort),
                };
            }));

        return new T.Ports { PortLines = portLines };
    }

    private static Compositor RenderVolumes(BoutiqueYamlSchema b, string relativePathToRoot)
    {
        if (b.Docker?.Volumes is null || b.Docker.Volumes.Count == 0)
        {
            var certsPath = relativePathToRoot == "." ? "./docker/certs" : $"{relativePathToRoot}/docker/certs";
            return new T.VolumesDefault { CertsPath = certsPath };
        }

        var volumeItems = new List<Compositor>();
        foreach (var v in b.Docker.Volumes)
        {
            if (IsDangerousVolumeSpec(v))
            {
                AnsiConsole.MarkupLine($"[yellow]Warning: skipping dangerous docker volume spec for {Markup.Escape(b.Name)}: {Markup.Escape(v)}[/]");
                continue;
            }
            volumeItems.Add(new T.VolumeLine { Spec = v });
        }

        if (volumeItems.Count == 0)
        {
            var fallbackCertsPath = relativePathToRoot == "." ? "./docker/certs" : $"{relativePathToRoot}/docker/certs";
            return new T.VolumesDefault { CertsPath = fallbackCertsPath };
        }

        return new T.Volumes { VolumeLines = Sequence.Lines(volumeItems) };
    }

    private static Compositor RenderHealthCheck(ResolvedBoutique resolved)
    {
        return new T.HealthCheck
        {
            Port = resolved.Health.HealthcheckPort,
            Path = SanitizeHealthPath(resolved.Health.ReadinessPath),
            Interval = resolved.Health.ReadinessIntervalSeconds,
            StartupDelay = resolved.Health.ReadinessStartupDelaySeconds,
            Timeout = resolved.Health.TimeoutSeconds,
            Retries = resolved.Health.Retries,
        };
    }

    private static Compositor? RenderResources(ResolvedBoutique resolved)
    {
        var limits = resolved.ResourceLimits;
        if (string.IsNullOrEmpty(limits.CpusLimit)
            && string.IsNullOrEmpty(limits.MemoryLimit))
        {
            return null;
        }

        var limitItems = new List<Compositor>();
        if (!string.IsNullOrEmpty(limits.CpusLimit))
        {
            limitItems.Add(new T.CpusLimit { Cpus = SanitizeScalar(limits.CpusLimit) });
        }
        if (!string.IsNullOrEmpty(limits.MemoryLimit))
        {
            limitItems.Add(new T.MemoryLimit { Memory = SanitizeScalar(limits.MemoryLimit) });
        }

        return new T.Resources
        {
            LimitLines = Sequence.Lines(limitItems),
        };
    }
}
