using Atelier.Build.Pipeline;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Atelier.Build.Discovery;

public class BoutiqueDiscoverer
{
    private readonly BuildContext _context;
    private readonly SubsystemDiscoverer _subsystemDiscoverer;

    private static readonly IDeserializer _yamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public BoutiqueDiscoverer(BuildContext context)
    {
        _context = context;
        _subsystemDiscoverer = new SubsystemDiscoverer(context);
    }

    public async Task<IReadOnlyList<BoutiqueDefinition>> DiscoverAsync()
    {

        var indexBoutiques = await DiscoverFromIndexAsync().ConfigureAwait(false);

        var srcBoutiques = await DiscoverFromSourceAsync().ConfigureAwait(false);

        var legacyBoutiques = await DiscoverLegacyAsync().ConfigureAwait(false);

        var combined = indexBoutiques.ToList();
        var seen = combined.Select(b => b.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var boutique in srcBoutiques.Concat(legacyBoutiques))
        {
            if (seen.Add(boutique.Name))
            {
                combined.Add(boutique);
            }
        }

        return combined.OrderBy(b => b.Name, StringComparer.Ordinal).ToList();
    }

    private async Task<IReadOnlyList<BoutiqueDefinition>> DiscoverFromIndexAsync()
    {
        var indexPath = Path.Combine(_context.SolutionRoot, "boutiques.yml");
        if (!File.Exists(indexPath))
        {
            return [];
        }

        var indexContent = await File.ReadAllTextAsync(indexPath).ConfigureAwait(false);
        var index = _yamlDeserializer.Deserialize<BoutiqueIndexSchema>(indexContent);
        if (index?.Boutiques is null
            || index.Boutiques.Count == 0)
        {
            return [];
        }

        var definitions = new List<BoutiqueDefinition>();

        foreach (var (name, relativePath) in index.Boutiques)
        {
            var smashFile = Path.Combine(_context.SolutionRoot, relativePath, "smash.yml");
            if (!File.Exists(smashFile))
            {
                if (_context.Verbose)
                {
                    Spectre.Console.AnsiConsole.MarkupLine($"[yellow]  Index entry '{name}' points at missing smash.yml: {Spectre.Console.Markup.Escape(smashFile)}[/]");
                }
                continue;
            }

            var definition = await ParseMinimalBoutiqueAsync(smashFile).ConfigureAwait(false);
            if (definition != null)
            {
                definitions.Add(definition);
            }
        }

        return definitions;
    }

        private async Task<IReadOnlyList<BoutiqueDefinition>> DiscoverFromSourceAsync()
    {
        var smashFiles = Directory.GetFiles(_context.SolutionRoot, "smash.yml", SearchOption.AllDirectories)
            .Where(f => !IsBuildOutputPath(f))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();
        var definitions = new List<BoutiqueDefinition>();

        if (_context.Verbose)
        {
            Spectre.Console.AnsiConsole.MarkupLine($"[dim]  Found {smashFiles.Length} smash.yml under {Spectre.Console.Markup.Escape(_context.SolutionRoot)}[/]");
        }

        foreach (var filePath in smashFiles)
        {
            var definition = await ParseMinimalBoutiqueAsync(filePath).ConfigureAwait(false);
            if (definition != null)
            {
                definitions.Add(definition);
                if (_context.Verbose)
                {
                    Spectre.Console.AnsiConsole.MarkupLine($"[green]    + {definition.Name}[/]");
                }
            }
        }

        return definitions;
    }

        private async Task<IReadOnlyList<BoutiqueDefinition>> DiscoverLegacyAsync()
    {
        var boutiquesDir = Path.Combine(_context.SolutionRoot, "boutiques");
        if (!Directory.Exists(boutiquesDir))
        {
            return [];
        }

        var boutiqueFiles = Directory.GetFiles(boutiquesDir, "boutique.yml", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();
        var definitions = new List<BoutiqueDefinition>();

        foreach (var filePath in boutiqueFiles)
        {
            var definition = await ParseLegacyBoutiqueFileAsync(filePath).ConfigureAwait(false);
            if (definition != null)
            {
                definitions.Add(definition);
            }
        }

        return definitions;
    }

        private async Task<BoutiqueDefinition?> ParseMinimalBoutiqueAsync(string smashPath)
    {
        var smashContent = await File.ReadAllTextAsync(smashPath).ConfigureAwait(false);
        var smashSchema = _yamlDeserializer.Deserialize<SmashYamlSchema>(smashContent);

        if (smashSchema is null
            || string.IsNullOrEmpty(smashSchema.Solution))
        {
            if (_context.Verbose)
            {
                Spectre.Console.AnsiConsole.MarkupLine($"[yellow]  Skipping smash.yml without a solution: {Spectre.Console.Markup.Escape(smashPath)}[/]");
            }
            return null;
        }

        var directory = Path.GetDirectoryName(smashPath)!;
        var subsystemName = Path.GetFileName(directory);

        var minimal = new MinimalBoutiqueSchema();
        var boutiquePath = Path.Combine(directory, "boutique.yml");
        if (File.Exists(boutiquePath))
        {
            var boutiqueContent = await File.ReadAllTextAsync(boutiquePath).ConfigureAwait(false);
            minimal = _yamlDeserializer.Deserialize<MinimalBoutiqueSchema>(boutiqueContent) ?? new MinimalBoutiqueSchema();
        }

        var subsystem = new SubsystemDefinition
        {
            Name = smashSchema.Name ?? subsystemName,
            Description = smashSchema.Description ?? $"{subsystemName} Subsystem",
            Directory = directory,
            SolutionPath = Path.Combine(directory, smashSchema.Solution),
            Dependencies = smashSchema.Dependencies?.ToList() ?? []
        };

        var boutiqueName = minimal.Name ?? smashSchema.Name ?? $"atelier-{subsystemName}";
        var solutionPath = subsystem.SolutionPath;

        if (_context.Verbose)
        {
            Spectre.Console.AnsiConsole.MarkupLine($"[dim]  Subsystem: {subsystem?.Name ?? "none"}, Solution: {solutionPath}[/]");
        }

        var hostProject = string.IsNullOrWhiteSpace(smashSchema.Host)
            ? null
            : Path.GetFullPath(Path.Combine(directory, smashSchema.Host));

        var outputDir = Path.Combine(_context.SolutionRoot, "boutiques", subsystemName.ToLowerInvariant());

        if (_context.Verbose)
        {
            Spectre.Console.AnsiConsole.MarkupLine($"[dim]  {subsystemName}: host {Spectre.Console.Markup.Escape(hostProject ?? "none")}[/]");
        }

        return new BoutiqueDefinition
        {
            Name = boutiqueName,
            Version = "1.0.0",
            Description = subsystem?.Description ?? $"{subsystemName} Boutique Host",
            SourceDirectory = directory,
            OutputDirectory = outputDir,
            ProjectPath = hostProject ?? Path.GetFullPath(solutionPath),
            HostProjectPath = hostProject,
            SubsystemName = subsystemName,
            Dependencies = subsystem?.Dependencies ?? [],
            Products = minimal.Products ?? [],
            Ports = new PortConfiguration
            {
                Http = minimal.Ports.Http,
                Grpc = minimal.Ports.Grpc,
                Metrics = minimal.Ports.Metrics,
                Gravity = minimal.Ports.GetGravityPort()
            },
            Infrastructure = new InfrastructureConfiguration
            {
                PostgresEnabled = minimal.Infrastructure?.Postgres ?? false,
                RedisEnabled = minimal.Infrastructure?.Redis ?? false,
                HangfireEnabled = minimal.Infrastructure?.Hangfire ?? false,
                SignalREnabled = minimal.Infrastructure?.SignalR ?? false
            },
            Build = new BuildSettings
            {
                Configuration = "Release",
                TreatWarningsAsErrors = false
            },
            Docker = new DockerSettings
            {
                EnvironmentVariables = minimal.Environment ?? new Dictionary<string, string>(),
                Volumes = minimal.Volumes ?? [],
                Command = minimal.Command,
                HealthCheck = minimal.Healthcheck != null
                    ? new HealthCheckConfiguration
                    {
                        Path = minimal.Healthcheck.Path,
                        Port = minimal.Healthcheck.Port
                    }
                    : new HealthCheckConfiguration()
            },
            Capabilities = minimal.Capabilities != null
                ? new CapabilitiesConfiguration
                {
                    RestEnabled = minimal.Capabilities.Rest,
                    GrpcEnabled = minimal.Capabilities.Grpc,
                    WebSocketEnabled = minimal.Capabilities.WebSocket
                }
                : null,
            Resources = minimal.Resources != null
                ? new ResourcesConfiguration
                {
                    MaxMemoryBytes = minimal.Resources.MaxMemoryBytes,
                    MaxCpuPercent = minimal.Resources.MaxCpuPercent
                }
                : null
        };
    }

        private async Task<BoutiqueDefinition?> ParseLegacyBoutiqueFileAsync(string filePath)
    {
        var yamlContent = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
        var schema = _yamlDeserializer.Deserialize<BoutiqueYamlSchema>(yamlContent);

        if (string.IsNullOrWhiteSpace(schema.Name))
        {
            return null;
        }

        var projectDirectory = Path.GetDirectoryName(filePath)!;
        var projectFile = FindProjectFile(projectDirectory);

        if (projectFile == null)
        {
            var expectedName = $"Atelier.Host.{Path.GetFileName(projectDirectory)}.csproj";
            projectFile = Path.Combine(projectDirectory, expectedName);
        }

        return new BoutiqueDefinition
        {
            Name = schema.Name,
            Version = schema.Version,
            Description = schema.Description,
            ProjectPath = projectFile,
            SourceDirectory = null,
            OutputDirectory = projectDirectory,
            Dependencies = schema.Dependencies ?? [],
            ProjectReferences = schema.ProjectReferences ?? [],
            GrpcServices = schema.GrpcServices?.Select(g => new GrpcServiceDeclaration
            {
                ServiceName = Path.GetFileNameWithoutExtension(g.Implementation),
                Implementation = g.Implementation,
                Assembly = g.Assembly
            }).ToList() ?? [],
            Ports = schema.Kestrel?.Endpoints != null
                ? new PortConfiguration
                {
                    Http = schema.Kestrel.Endpoints.FirstOrDefault(e => e.Name == "http")?.Port ?? Atelier.Build.Utils.DefaultPorts.Http,
                    Grpc = schema.Kestrel.Endpoints.FirstOrDefault(e => e.Name == "grpc")?.Port ?? Atelier.Build.Utils.DefaultPorts.Grpc,
                    Metrics = schema.Kestrel.Endpoints.FirstOrDefault(e => e.Name == "metrics")?.Port ?? Atelier.Build.Utils.DefaultPorts.Metrics
                }
                : new PortConfiguration(),
            Infrastructure = schema.Infrastructure != null
                ? new InfrastructureConfiguration
                {
                    PostgresEnabled = schema.Infrastructure.Postgres?.Enabled ?? false,
                    RedisEnabled = schema.Infrastructure.Redis?.Enabled ?? false,
                    HangfireEnabled = schema.Infrastructure.Hangfire?.Enabled ?? false,
                    SignalREnabled = schema.Infrastructure.SignalR?.Enabled ?? false
                }
                : new InfrastructureConfiguration(),
            Build = schema.Build != null
                ? new BuildSettings
                {
                    Configuration = schema.Build.Configuration,
                    TreatWarningsAsErrors = schema.Build.TreatWarningsAsErrors,
                    AdditionalMsBuildArgs = schema.Build.MsBuildArgs ?? []
                }
                : new BuildSettings(),
            Capabilities = schema.Capabilities != null
                ? new CapabilitiesConfiguration
                {
                    RestEnabled = schema.Capabilities.Rest?.Enabled,
                    GrpcEnabled = schema.Capabilities.Grpc?.Enabled,
                    WebSocketEnabled = schema.Capabilities.WebSocket?.Enabled
                }
                : null,
            Resources = schema.Resources != null
                ? new ResourcesConfiguration
                {
                    MaxMemoryBytes = schema.Resources.MaxMemoryBytes,
                    MaxCpuPercent = schema.Resources.MaxCpuPercent
                }
                : null
        };
    }

    private static string? FindProjectFile(string directory)
    {
        var csprojFiles = Directory.GetFiles(directory, "*.csproj");
        return csprojFiles.Length == 1 ? csprojFiles[0] : null;
    }

    private static bool IsBuildOutputPath(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var segment in segments)
        {
            if (segment.Equals("bin", StringComparison.Ordinal)
                || segment.Equals("obj", StringComparison.Ordinal)
                || segment.Equals(".artifacts", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
