using Templar.Rendering;

namespace Atelier.Build.Generation;

public sealed record ContainerServiceEntry(string ServiceName, string DockerfileName, string ImageName);

public static class ContainerComposeWriter
{
    public static async Task<string> WriteAsync(
        string solutionRoot,
        string outputFileName,
        IReadOnlyList<ContainerServiceEntry> entries,
        Func<ContainerServiceEntry, Compositor> entryFactory,
        Func<IComposable, Compositor> composeFactory)
    {
        var outputPath = Path.Combine(solutionRoot, outputFileName);

        var services = Sequence.BlankLines(entries.Select(entryFactory));

        var content = composeFactory(services).Render();

        await File.WriteAllTextAsync(outputPath, content).ConfigureAwait(false);
        return outputPath;
    }
}
