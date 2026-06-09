using Atelier.Build.Discovery;
using Atelier.Build.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Atelier.Build.Services;

public class BoutiqueDiscoveryService : IBoutiqueDiscoveryService
{
    private readonly ILogger<BoutiqueDiscoveryService> _logger;

    private static readonly IDeserializer _yamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public BoutiqueDiscoveryService(ILogger<BoutiqueDiscoveryService>? logger = null)
    {
        _logger = logger ?? NullLogger<BoutiqueDiscoveryService>.Instance;
    }

    public async Task<IReadOnlyList<BoutiqueDefinition>> DiscoverBoutiquesAsync(
        string solutionRoot,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Discovering boutiques in {SolutionRoot}", solutionRoot);

        var boutiqueFiles = Directory.GetFiles(
            solutionRoot,
            "boutique.yml",
            SearchOption.AllDirectories);

        _logger.LogDebug("Found {Count} boutique.yml files", boutiqueFiles.Length);

        var definitions = new List<BoutiqueDefinition>();

        foreach (var filePath in boutiqueFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var definition = await ParseBoutiqueAsync(filePath, cancellationToken).ConfigureAwait(false);
            if (definition != null)
            {
                definitions.Add(definition);
                _logger.LogDebug("Parsed boutique: {Name}", definition.Name);
            }
        }

        _logger.LogInformation("Discovered {Count} boutiques", definitions.Count);

        return definitions;
    }

    public async Task<BoutiqueDefinition?> ParseBoutiqueAsync(
        string boutiqueYamlPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(boutiqueYamlPath))
        {
            _logger.LogWarning("Boutique file not found: {Path}", boutiqueYamlPath);
            return null;
        }

        var yamlContent = await File.ReadAllTextAsync(boutiqueYamlPath, cancellationToken).ConfigureAwait(false);
        var schema = _yamlDeserializer.Deserialize<BoutiqueYamlSchema>(yamlContent);

        if (string.IsNullOrWhiteSpace(schema.Name))
        {
            _logger.LogWarning("Boutique file has no name: {Path}", boutiqueYamlPath);
            return null;
        }

        var projectDirectory = Path.GetDirectoryName(boutiqueYamlPath)!;
        var projectFile = FindProjectFile(projectDirectory);

        if (projectFile == null)
        {
            _logger.LogWarning("No .csproj file found for boutique: {Name} in {Directory}", schema.Name, projectDirectory);
            return null;
        }

        return new BoutiqueDefinition
        {
            Name = schema.Name,
            Version = schema.Version,
            Description = schema.Description,
            ProjectPath = projectFile,
            Dependencies = schema.Dependencies ?? [],
            Offerings = schema.Offerings?.Select(o => new OfferingDeclaration
            {
                Name = o.Name,
                Description = o.Description,
                Enabled = o.Enabled,
                Configuration = o.Config ?? new Dictionary<string, object>()
            }).ToList() ?? [],
            Build = schema.Build != null
                ? new BuildSettings
                {
                    Configuration = schema.Build.Configuration,
                    TreatWarningsAsErrors = schema.Build.TreatWarningsAsErrors,
                    AdditionalMsBuildArgs = schema.Build.MsBuildArgs ?? []
                }
                : new BuildSettings(),
            Docker = schema.Docker != null
                ? new DockerSettings
                {
                    BaseImage = schema.Docker.BaseImage,
                    ExposedPorts = schema.Docker.Ports ?? [],
                    EnvironmentVariables = schema.Docker.Env ?? new Dictionary<string, string>()
                }
                : null
        };
    }

    public async Task<BoutiqueYamlSchema> ParseYamlSchemaAsync(
        string boutiqueYamlPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(boutiqueYamlPath))
        {
            throw new FileNotFoundException($"Boutique file not found: {boutiqueYamlPath}");
        }

        var yamlContent = await File.ReadAllTextAsync(boutiqueYamlPath, cancellationToken).ConfigureAwait(false);
        return _yamlDeserializer.Deserialize<BoutiqueYamlSchema>(yamlContent);
    }

    private static string? FindProjectFile(string directory)
    {
        var csprojFiles = Directory.GetFiles(directory, "*.csproj");
        return csprojFiles.Length == 1 ? csprojFiles[0] : null;
    }
}
