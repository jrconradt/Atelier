using Atelier.Build.Discovery;
using Atelier.Build.Pipeline;
using Templar.Rendering;
using T = Atelier.Build.Templates.Benchmark;

namespace Atelier.Build.Generation;

public class BenchmarkDockerfileGenerator
{
    private readonly BuildContext _context;

    public BenchmarkDockerfileGenerator(BuildContext context)
    {
        _context = context;
    }

    public async Task<string> GenerateAsync(BenchmarkDefinition definition)
    {
        var outputPath = Path.Combine(_context.SolutionRoot, "boutiques", definition.DockerfileName);

        var projectRelativePath = definition.ProjectPath
            .Replace(_context.SolutionRoot + Path.DirectorySeparatorChar, "")
            .Replace("\\", "/");

        var dockerfile = new T.Dockerfile
        {
            ProjectName = definition.ProjectName,
            SubsystemName = definition.SubsystemName,
            ProjectRelativePath = projectRelativePath,
            SdkImage = DockerImagePolicy.SdkImage(definition.TargetFramework, definition.SdkImageDigest),
            RuntimeImage = DockerImagePolicy.RuntimeImage(definition.TargetFramework, definition.RuntimeImageDigest),
        }.Render();

        await File.WriteAllTextAsync(outputPath, dockerfile).ConfigureAwait(false);
        return outputPath;
    }

    public async Task<string> GenerateDockerComposeAsync(IReadOnlyList<BenchmarkDefinition> definitions)
    {
        var entries = definitions
            .Select(def => new ContainerServiceEntry(def.ServiceName, def.DockerfileName, def.ImageName))
            .ToList();

        return await ContainerComposeWriter.WriteAsync(
            _context.SolutionRoot,
            "docker-compose.benchmarks.yml",
            entries,
            entry => new T.ServiceEntry
            {
                ServiceName = entry.ServiceName,
                DockerfileName = entry.DockerfileName,
                ImageName = entry.ImageName,
            },
            services => new T.DockerCompose { Services = services }).ConfigureAwait(false);
    }
}
