using Atelier.Build.Pipeline;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Atelier.Build.Discovery;

public class SubsystemDiscoverer
{
    private readonly BuildContext _context;

    private static readonly IDeserializer _yamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private IReadOnlyList<SubsystemDefinition>? _cachedDefinitions;
    private Dictionary<string, SubsystemDefinition>? _byName;

    public SubsystemDiscoverer(BuildContext context)
    {
        _context = context;
    }

        public async Task<IReadOnlyList<SubsystemDefinition>> DiscoverAsync()
    {
        if (_cachedDefinitions != null)
        {
            return _cachedDefinitions;
        }

        var srcDir = Path.Combine(_context.SolutionRoot, "src");
        if (!Directory.Exists(srcDir))
        {
            _cachedDefinitions = [];
            _byName = new Dictionary<string, SubsystemDefinition>(StringComparer.OrdinalIgnoreCase);
            return _cachedDefinitions;
        }

        var smashFiles = Directory.GetFiles(srcDir, "smash.yml", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();
        var definitions = new List<SubsystemDefinition>();

        foreach (var filePath in smashFiles)
        {
            var definition = await ParseSmashFileAsync(filePath).ConfigureAwait(false);
            if (definition != null)
            {
                definitions.Add(definition);
            }
        }

        var index = new Dictionary<string, SubsystemDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions)
        {
            if (!index.ContainsKey(definition.Name))
            {
                index[definition.Name] = definition;
            }
        }

        _cachedDefinitions = definitions;
        _byName = index;
        return _cachedDefinitions;
    }

        public async Task<SubsystemDefinition?> GetByNameAsync(string name)
    {
        await DiscoverAsync().ConfigureAwait(false);
        return _byName != null && _byName.TryGetValue(name, out var definition)
            ? definition
            : null;
    }

    private async Task<SubsystemDefinition?> ParseSmashFileAsync(string filePath)
    {
        var yamlContent = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
        var schema = _yamlDeserializer.Deserialize<SmashYamlSchema>(yamlContent);

        if (schema is null
            || string.IsNullOrWhiteSpace(schema.Name))
        {
            return null;
        }

        var validationErrors = schema.Validate();
        if (validationErrors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Invalid smash.yml at {filePath}: {string.Join("; ", validationErrors)}");
        }

        var directory = Path.GetDirectoryName(filePath)!;
        var solutionPath = Path.Combine(directory, schema.Solution);

        return new SubsystemDefinition
        {
            Name = schema.Name,
            Description = schema.Description,
            Directory = directory,
            SolutionPath = File.Exists(solutionPath) ? solutionPath : null,
            Dependencies = schema.Dependencies,
            TestProjects = schema.Test?.Projects ?? [],
            BenchmarkProject = schema.Benchmark?.Project,
            BuildConfiguration = schema.Build?.Configuration ?? "Debug",
            ParallelBuild = schema.Build?.Parallel ?? true,
            PreBuild = schema.PreBuild,
            PostBuild = schema.PostBuild,
            PostTest = schema.PostTest,
            Test = schema.Test,
            Benchmark = schema.Benchmark,
            Build = schema.Build
        };
    }
}

public class SubsystemDefinition
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string Directory { get; init; }
    public string? SolutionPath { get; init; }
    public IReadOnlyList<string> Dependencies { get; init; } = [];
    public IReadOnlyList<string> TestProjects { get; init; } = [];
    public string? BenchmarkProject { get; init; }
    public string BuildConfiguration { get; init; } = "Debug";
    public bool ParallelBuild { get; init; } = true;
    public PreBuildConfig? PreBuild { get; init; }
    public PreBuildConfig? PostBuild { get; init; }
    public PreBuildConfig? PostTest { get; init; }
    public SmashTestConfig? Test { get; init; }
    public SmashBenchmarkConfig? Benchmark { get; init; }
    public SmashBuildConfig? Build { get; init; }
}
