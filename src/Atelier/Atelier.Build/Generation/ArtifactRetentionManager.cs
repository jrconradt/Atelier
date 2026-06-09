using System.Text.Json;
using Atelier.Build.Pipeline;
using Spectre.Console;

namespace Atelier.Build.Generation;

public class ArtifactRetentionManager
{
    private readonly BuildContext _context;
    private const int DEFAULT_RETENTION_COUNT = 3;

    public ArtifactRetentionManager(BuildContext context)
    {
        _context = context;
    }

    public Task EnforceRetentionAsync()
    {
        var protectedPaths = LoadDeployedArtifactPaths();

        EnforceRetention(_context.DiagramOutputDirectory,
                         "*.mmd",
                         DEFAULT_RETENTION_COUNT,
                         protectedPaths);
        EnforceRetention(_context.BuildOutputDirectory,
                         "*",
                         DEFAULT_RETENTION_COUNT,
                         protectedPaths,
                         isDirectory: true);

        return Task.CompletedTask;
    }

    private HashSet<string> LoadDeployedArtifactPaths()
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var registryDirectory = Path.Combine(Directory.GetCurrentDirectory(), ".atelier", "deployments");

        if (!Directory.Exists(registryDirectory))
        {
            return paths;
        }

        foreach (var recordPath in Directory.GetFiles(registryDirectory, "*.json"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(recordPath));
                if (doc.RootElement.TryGetProperty("DockerComposePath", out var composePath)
                    && composePath.GetString() is { } value)
                {
                    var dir = Path.GetDirectoryName(Path.GetFullPath(value));
                    if (dir is not null)
                    {
                        paths.Add(dir);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                if (_context.Verbose)
                {
                    AnsiConsole.MarkupLine($"[dim]Could not read deployment record {Markup.Escape(recordPath)}: {Markup.Escape(ex.Message)}[/]");
                }
            }
        }

        return paths;
    }

    private void EnforceRetention(string directory,
                                  string pattern,
                                  int retentionCount,
                                  HashSet<string> protectedPaths,
                                  bool isDirectory = false)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        IEnumerable<FileSystemInfo> items;

        if (isDirectory)
        {
            items = new DirectoryInfo(directory)
                .GetDirectories()
                .Where(d => d.Name != "latest")
                .OrderByDescending(d => d.CreationTimeUtc);
        }
        else
        {
            items = new DirectoryInfo(directory)
                .GetFiles(pattern)
                .Where(f => !f.Name.Contains("latest"))
                .OrderByDescending(f => f.CreationTimeUtc);
        }

        var toDelete = items
            .Skip(retentionCount)
            .Where(item => !protectedPaths.Contains(Path.GetFullPath(item.FullName)))
            .ToList();

        foreach (var item in toDelete)
        {
            try
            {
                if (item is DirectoryInfo dir)
                {
                    dir.Delete(recursive: true);
                }
                else
                {
                    item.Delete();
                }

                if (_context.Verbose)
                {
                    AnsiConsole.MarkupLine($"[dim]Removed old artifact: {item.Name}[/]");
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[yellow]Warning: Could not delete {item.Name}: {ex.Message}[/]");
            }
        }
    }
}
