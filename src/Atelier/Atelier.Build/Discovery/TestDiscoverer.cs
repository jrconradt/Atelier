using Atelier.Build.Pipeline;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Atelier.Build.Discovery;

public class TestDiscoverer
{
    private readonly BuildContext _context;
    private readonly IDeserializer _yamlDeserializer;

    public TestDiscoverer(BuildContext context)
    {
        _context = context;
        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

        public async Task<IReadOnlyList<TestDefinition>> DiscoverAsync()
    {
        var srcDir = Path.Combine(_context.SolutionRoot, "src");
        if (!Directory.Exists(srcDir))
        {
            return [];
        }

        var smashFiles = Directory.GetFiles(srcDir, "smash.yml", SearchOption.AllDirectories);
        var discovered = new List<DiscoveredSmash>();

        foreach (var smashPath in smashFiles)
        {
            var yamlContent = await File.ReadAllTextAsync(smashPath).ConfigureAwait(false);
            var smashSchema = _yamlDeserializer.Deserialize<SmashYamlSchema>(yamlContent);

            if (smashSchema is null)
            {
                continue;
            }

            var directory = Path.GetDirectoryName(smashPath)!;
            var relativeDirectory = Path.GetRelativePath(srcDir, directory).Replace(Path.DirectorySeparatorChar, '/');

            discovered.Add(new DiscoveredSmash(smashSchema, directory, relativeDirectory));
        }

        var directoryByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in discovered)
        {
            directoryByName[entry.Schema.Name] = entry.RelativeDirectory;
        }

        var definitions = new List<TestDefinition>();

        foreach (var entry in discovered)
        {
            var smashSchema = entry.Schema;

            if (smashSchema.Test?.Projects == null || smashSchema.Test.Projects.Count == 0)
            {
                continue;
            }

            var hasValidTests = smashSchema.Test.Projects.Any(projectName =>
            {
                var projectPath = Path.Combine(entry.Directory, projectName, $"{projectName}.csproj");
                return File.Exists(projectPath);
            });

            if (!hasValidTests)
            {
                continue;
            }

            var subsystemName = smashSchema.Name;
            var dependencyDirectories = new List<string>();
            foreach (var dependency in smashSchema.Dependencies ?? [])
            {
                if (directoryByName.TryGetValue(dependency, out var dependencyDirectory)
                    && !string.Equals(dependencyDirectory, entry.RelativeDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    dependencyDirectories.Add(dependencyDirectory);
                }
            }

            var definition = new TestDefinition
            {
                SubsystemName = subsystemName,
                TestProjectCount = smashSchema.Test.Projects.Count,
                SourceDirectory = entry.Directory,
                Directory = entry.RelativeDirectory,
                Dependencies = smashSchema.Dependencies ?? [],
                DependencyDirectories = dependencyDirectories,
                SolutionFileName = smashSchema.Solution,
                DockerfileName = $"Dockerfile.{subsystemName}-tests",
                ImageName = $"atelier/{subsystemName}-tests",
                ServiceName = $"{subsystemName}-tests",
                TargetFramework = smashSchema.Build?.TargetFramework ?? "net10.0",
                SdkImageDigest = smashSchema.Build?.SdkImageDigest
            };

            definitions.Add(definition);
        }

        return definitions;
    }

    private readonly record struct DiscoveredSmash(SmashYamlSchema Schema, string Directory, string RelativeDirectory);
}

public record TestDefinition
{
    public required string SubsystemName { get; init; }
    public required int TestProjectCount { get; init; }
    public required string SourceDirectory { get; init; }
    public required string Directory { get; init; }
    public required IReadOnlyList<string> Dependencies { get; init; }
    public required IReadOnlyList<string> DependencyDirectories { get; init; }
    public required string SolutionFileName { get; init; }
    public required string DockerfileName { get; init; }
    public required string ImageName { get; init; }
    public required string ServiceName { get; init; }
    public required string TargetFramework { get; init; }
    public string? SdkImageDigest { get; init; }
}
