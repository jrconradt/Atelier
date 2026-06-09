using Atelier.Build.Discovery;
using Atelier.Build.Pipeline;
using Templar.Rendering;
using T = Atelier.Build.Templates.TestSuite;

namespace Atelier.Build.Generation;

public class TestDockerfileGenerator
{
    private readonly BuildContext _context;

    public TestDockerfileGenerator(BuildContext context)
    {
        _context = context;
    }

    public async Task<string> GenerateAsync(TestDefinition definition)
    {
        var outputPath = Path.Combine(_context.SolutionRoot, "boutiques", definition.DockerfileName);

        var subsystemDir = definition.Directory;

        var content = new T.Dockerfile
        {
            SubsystemName = definition.SubsystemName,
            SubsystemDir = subsystemDir,
            SolutionFileName = definition.SolutionFileName,
            DependencyCopies = BuildDependencyCopies(definition),
            SdkImage = DockerImagePolicy.SdkImage(definition.TargetFramework, definition.SdkImageDigest),
        }.Render();

        await File.WriteAllTextAsync(outputPath, content).ConfigureAwait(false);
        return outputPath;
    }

    public async Task<string> GenerateDockerComposeAsync(IReadOnlyList<TestDefinition> definitions)
    {
        var entries = definitions
            .Select(def => new ContainerServiceEntry(def.ServiceName, def.DockerfileName, def.ImageName))
            .ToList();

        return await ContainerComposeWriter.WriteAsync(
            _context.SolutionRoot,
            "docker-compose.tests.yml",
            entries,
            entry => new T.ServiceEntry
            {
                ServiceName = entry.ServiceName,
                DockerfileName = entry.DockerfileName,
                ImageName = entry.ImageName,
            },
            services => new T.DockerCompose { Services = services }).ConfigureAwait(false);
    }

    private static IComposable BuildDependencyCopies(TestDefinition definition)
    {
        var copies = new List<Compositor>();

        foreach (var depDir in definition.DependencyDirectories)
        {
            copies.Add(new T.DependencyCopy { Dir = depDir });
        }

        if (copies.Count == 0)
        {
            return new T.NoDependencies();
        }
        return Sequence.Lines(copies);
    }
}
