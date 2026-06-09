using Atelier.Build.Discovery;
using Spectre.Console;

namespace Atelier.Build.Pipeline;

public sealed class DirectBuildRunner
{
    private readonly BuildContext _context;

    public DirectBuildRunner(BuildContext context)
    {
        _context = context;
    }

    public async Task<BuildResult> ExecuteAsync(List<string> artifacts)
    {
        var path = _context.ProjectPath!;
        var fullPath = Path.IsPathRooted(path) ? path : Path.Combine(_context.WorkingDirectory, path);

        if (!File.Exists(fullPath))
        {
            return BuildResult.Failure($"File not found: {fullPath}");
        }

        var isSolution = fullPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase);
        var isProject = fullPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);

        if (!isSolution && !isProject)
        {
            return BuildResult.Failure("Path must be a .sln or .csproj file");
        }

        var name = Path.GetFileNameWithoutExtension(fullPath);
        LogPhase($"Building {(isSolution ? "solution" : "project")}: {name}");

        if (_context.DryRun)
        {
            AnsiConsole.MarkupLine($"[yellow]Dry run - would build: {fullPath}[/]");
            return BuildResult.Success(artifacts, []);
        }

        var outputDir = Path.Combine(_context.BuildOutputDirectory, "assemblies");
        Directory.CreateDirectory(outputDir);

        var compiler = new BoutiqueCompiler(_context);

        var definition = new BoutiqueDefinition
        {
            Name = name,
            Version = "1.0.0",
            ProjectPath = fullPath,
            Offerings = [],
            Dependencies = [],
            Build = new BuildSettings { Configuration = "Debug" },
            Docker = new DockerSettings()
        };

        var manifest = await compiler.CompileAsync(definition).ConfigureAwait(false);
        artifacts.Add(manifest.OutputAssembly);

        return BuildResult.Success(artifacts, [manifest]);
    }

    private void LogPhase(string message)
    {
        if (_context.Verbose)
        {
            AnsiConsole.MarkupLine($"[blue]▸[/] {message}");
        }
    }
}
