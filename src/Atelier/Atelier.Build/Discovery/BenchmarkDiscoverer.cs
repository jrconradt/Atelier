using Atelier.Build.Pipeline;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Atelier.Build.Discovery;

public class BenchmarkDiscoverer
{
    private readonly BuildContext _context;
    private readonly IDeserializer _yamlDeserializer;

    public BenchmarkDiscoverer(BuildContext context)
    {
        _context = context;
        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

        public async Task<IReadOnlyList<BenchmarkDefinition>> DiscoverAsync()
    {
        var srcDir = Path.Combine(_context.SolutionRoot, "src");
        if (!Directory.Exists(srcDir))
        {
            return [];
        }

        var smashFiles = Directory.GetFiles(srcDir, "smash.yml", SearchOption.AllDirectories);
        var definitions = new List<BenchmarkDefinition>();

        foreach (var smashPath in smashFiles)
        {
            var yamlContent = await File.ReadAllTextAsync(smashPath).ConfigureAwait(false);
            var smashSchema = _yamlDeserializer.Deserialize<SmashYamlSchema>(yamlContent);

            if (smashSchema?.Benchmark?.Project != null)
            {
                var directory = Path.GetDirectoryName(smashPath)!;
                var subsystemName = smashSchema.Name;
                var benchmarkProjectName = smashSchema.Benchmark.Project;

                var definition = new BenchmarkDefinition
                {
                    SubsystemName = subsystemName,
                    ProjectName = benchmarkProjectName,
                    ProjectPath = Path.Combine(directory, benchmarkProjectName, $"{benchmarkProjectName}.csproj"),
                    SourceDirectory = directory,
                    DockerfileName = $"Dockerfile.{subsystemName}-bench",
                    ImageName = $"atelier/{subsystemName}-benchmarks",
                    ServiceName = $"{subsystemName}-bench",
                    TargetFramework = smashSchema.Build?.TargetFramework ?? "net10.0",
                    SdkImageDigest = smashSchema.Build?.SdkImageDigest,
                    RuntimeImageDigest = smashSchema.Build?.RuntimeImageDigest
                };

                if (File.Exists(definition.ProjectPath))
                {
                    definitions.Add(definition);
                }
            }
        }

        return definitions;
    }
}

public record BenchmarkDefinition
{
    public required string SubsystemName { get; init; }
    public required string ProjectName { get; init; }
    public required string ProjectPath { get; init; }
    public required string SourceDirectory { get; init; }
    public required string DockerfileName { get; init; }
    public required string ImageName { get; init; }
    public required string ServiceName { get; init; }
    public required string TargetFramework { get; init; }
    public string? SdkImageDigest { get; init; }
    public string? RuntimeImageDigest { get; init; }
}
