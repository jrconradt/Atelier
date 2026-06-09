using Spectre.Console;

namespace Atelier.Build.Pipeline;

public class ArtifactCleaner
{
    private readonly BuildContext _context;

    private static readonly string[] GeneratedFilePatterns =
    [
        "*.g.cs",
        "*.AssemblyAttributes.cs",
        "*.dll"
    ];

    private static readonly string[] RetainedGeneratedFiles =
    [
        "AssemblyLoader.g.cs"
    ];

    public ArtifactCleaner(BuildContext context)
    {
        _context = context;
    }

    public async Task CleanAsync(bool all, bool docker)
    {
        CleanOneDriveConflictDirectories();
        await CleanDotnetBuildAsync().ConfigureAwait(false);
        await CleanGeneratedSourceFilesAsync(all).ConfigureAwait(false);
        CleanBuildArtifacts(all);
        EnforceLogRetention();

        if (docker)
        {
            await CleanDockerImagesAsync().ConfigureAwait(false);
        }
    }

    private void CleanOneDriveConflictDirectories()
    {



        var srcDirectory = Path.Combine(_context.SolutionRoot, "src");
        var boutiquesDirectory = Path.Combine(_context.SolutionRoot, "boutiques");

        int removed = 0;

        removed += CleanNameClashDirectoriesIn(srcDirectory);

        if (Directory.Exists(boutiquesDirectory))
        {
            removed += CleanNameClashDirectoriesIn(boutiquesDirectory);
        }

        if (removed > 0)
        {
            AnsiConsole.MarkupLine($"[yellow]Cleaned {removed} OneDrive conflict directories[/]");
        }
    }

    private int CleanNameClashDirectoriesIn(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        int removed = 0;

        try
        {
            var conflictDirs = Directory.GetDirectories(directory, "*Name clash*", SearchOption.AllDirectories);

            foreach (var dir in conflictDirs)
            {
                try
                {
                    Directory.Delete(dir, recursive: true);
                    removed++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    AnsiConsole.MarkupLine($"[dim]  Could not delete {dir.EscapeMarkup()}: {ex.Message.EscapeMarkup()}[/]");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AnsiConsole.MarkupLine($"[dim]  Could not enumerate {directory.EscapeMarkup()}: {ex.Message.EscapeMarkup()}[/]");
        }

        return removed;
    }

    private Task CleanDotnetBuildAsync()
    {
        AnsiConsole.MarkupLine("[yellow]Cleaning .NET build artifacts...[/]");


        CleanBinObjDirectoriesManually();

        return Task.CompletedTask;
    }

    private void CleanBinObjDirectoriesManually()
    {
        var srcDirectory = Path.Combine(_context.SolutionRoot, "src");
        var testsDirectory = Path.Combine(_context.SolutionRoot, "tests");
        var toolsDirectory = Path.Combine(_context.SolutionRoot, "tools");

        long totalBytes = 0;
        int directoriesRemoved = 0;

        directoriesRemoved += CleanBinObjInDirectory(srcDirectory, ref totalBytes);

        if (Directory.Exists(testsDirectory))
        {
            directoriesRemoved += CleanBinObjInDirectory(testsDirectory, ref totalBytes);
        }

        if (Directory.Exists(toolsDirectory))
        {
            directoriesRemoved += CleanBinObjInDirectory(toolsDirectory, ref totalBytes);
        }

        if (directoriesRemoved > 0)
        {
            AnsiConsole.MarkupLine($"[green]  ✓[/] Removed {directoriesRemoved} bin/obj directories");
        }
        else
        {
            AnsiConsole.MarkupLine("[dim]  No bin/obj directories to clean[/]");
        }
    }

    private int CleanBinObjInDirectory(string directory, ref long totalBytes)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        int removed = 0;

        var binDirectories = Directory.GetDirectories(directory, "bin", SearchOption.AllDirectories);
        var objDirectories = Directory.GetDirectories(directory, "obj", SearchOption.AllDirectories);

        foreach (var dir in binDirectories.Concat(objDirectories))
        {
            try
            {
                Directory.Delete(dir, recursive: true);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AnsiConsole.MarkupLine($"[dim]  Could not delete {dir.EscapeMarkup()}: {ex.Message.EscapeMarkup()}[/]");
            }
        }

        return removed;
    }

    private Task CleanGeneratedSourceFilesAsync(bool all)
    {
        AnsiConsole.MarkupLine("[yellow]Cleaning generated source files...[/]");

        var srcDirectory = Path.Combine(_context.SolutionRoot, "src");
        var testsDirectory = Path.Combine(_context.SolutionRoot, "tests");

        int totalDeleted = 0;
        long totalBytes = 0;

        totalDeleted += CleanGeneratedFilesInObjDirectories(srcDirectory, ref totalBytes);

        if (Directory.Exists(testsDirectory))
        {
            totalDeleted += CleanGeneratedFilesInObjDirectories(testsDirectory, ref totalBytes);
        }

        if (all)
        {
            totalDeleted += CleanRetainedGeneratedFiles(srcDirectory, ref totalBytes);

            if (Directory.Exists(testsDirectory))
            {
                totalDeleted += CleanRetainedGeneratedFiles(testsDirectory, ref totalBytes);
            }
        }

        if (totalDeleted > 0)
        {
            var sizeStr = FormatBytes(totalBytes);
            AnsiConsole.MarkupLine($"[green]  ✓[/] Removed {totalDeleted} generated files ({sizeStr})");
        }
        else
        {
            AnsiConsole.MarkupLine("[dim]  No generated files to clean[/]");
        }

        return Task.CompletedTask;
    }

    private int CleanGeneratedFilesInObjDirectories(string directory, ref long totalBytes)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        int deleted = 0;

        var objDirectories = Directory.GetDirectories(directory, "obj", SearchOption.AllDirectories);

        foreach (var objDir in objDirectories)
        {
            foreach (var pattern in GeneratedFilePatterns)
            {
                try
                {
                    var files = Directory.GetFiles(objDir, pattern, SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        try
                        {
                            var fileInfo = new FileInfo(file);
                            totalBytes += fileInfo.Length;
                            File.Delete(file);
                            deleted++;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            AnsiConsole.MarkupLine($"[dim]  Could not delete {file.EscapeMarkup()}: {ex.Message.EscapeMarkup()}[/]");
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    AnsiConsole.MarkupLine($"[dim]  Could not enumerate {pattern.EscapeMarkup()} in {objDir.EscapeMarkup()}: {ex.Message.EscapeMarkup()}[/]");
                }
            }

            var generatedDir = Path.Combine(objDir, "Debug", "net10.0", "generated");
            if (Directory.Exists(generatedDir))
            {
                try
                {
                    var dirInfo = new DirectoryInfo(generatedDir);
                    totalBytes += GetDirectorySize(dirInfo);
                    deleted += Directory.GetFiles(generatedDir, "*", SearchOption.AllDirectories).Length;
                    Directory.Delete(generatedDir, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    AnsiConsole.MarkupLine($"[dim]  Could not delete {generatedDir.EscapeMarkup()}: {ex.Message.EscapeMarkup()}[/]");
                }
            }

            var generatedDirRelease = Path.Combine(objDir, "Release", "net10.0", "generated");
            if (Directory.Exists(generatedDirRelease))
            {
                try
                {
                    var dirInfo = new DirectoryInfo(generatedDirRelease);
                    totalBytes += GetDirectorySize(dirInfo);
                    deleted += Directory.GetFiles(generatedDirRelease, "*", SearchOption.AllDirectories).Length;
                    Directory.Delete(generatedDirRelease, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    AnsiConsole.MarkupLine($"[dim]  Could not delete {generatedDirRelease.EscapeMarkup()}: {ex.Message.EscapeMarkup()}[/]");
                }
            }
        }

        return deleted;
    }

    private int CleanRetainedGeneratedFiles(string directory, ref long totalBytes)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        int deleted = 0;

        foreach (var retainedFile in RetainedGeneratedFiles)
        {
            try
            {
                var files = Directory.GetFiles(
                    directory,
                    retainedFile,
                    SearchOption.AllDirectories);

                foreach (var file in files)
                {
                    if (Atelier.Build.Utils.PathSegments.IsUnderBinOrObj(file))
                    {
                        continue;
                    }

                    try
                    {
                        var fileInfo = new FileInfo(file);
                        totalBytes += fileInfo.Length;
                        File.Delete(file);
                        deleted++;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        AnsiConsole.MarkupLine($"[dim]  Could not delete {file.EscapeMarkup()}: {ex.Message.EscapeMarkup()}[/]");
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AnsiConsole.MarkupLine($"[dim]  Could not enumerate {retainedFile.EscapeMarkup()} in {directory.EscapeMarkup()}: {ex.Message.EscapeMarkup()}[/]");
            }
        }

        return deleted;
    }

    private void CleanBuildArtifacts(bool all)
    {
        var artifactDir = _context.BuildOutputDirectory;

        if (!Directory.Exists(artifactDir))
        {
            AnsiConsole.MarkupLine("[dim]No build artifacts to clean[/]");
            return;
        }

        AnsiConsole.MarkupLine("[yellow]Cleaning build artifacts...[/]");

        if (all)
        {
            var size = GetDirectorySize(new DirectoryInfo(artifactDir));
            Directory.Delete(artifactDir, recursive: true);
            AnsiConsole.MarkupLine($"[green]  ✓[/] Removed {artifactDir} ({FormatBytes(size)})");
        }
        else
        {
            long totalSize = 0;
            int itemsRemoved = 0;

            var assembliesDir = Path.Combine(artifactDir, "assemblies");
            if (Directory.Exists(assembliesDir))
            {
                totalSize += GetDirectorySize(new DirectoryInfo(assembliesDir));
                Directory.Delete(assembliesDir, recursive: true);
                itemsRemoved++;
            }

            var latestDir = Path.Combine(artifactDir, "latest");
            if (Directory.Exists(latestDir))
            {
                totalSize += GetDirectorySize(new DirectoryInfo(latestDir));
                Directory.Delete(latestDir, recursive: true);
                itemsRemoved++;
            }

            var manifestFile = Path.Combine(artifactDir, "requisite-manifest.json");
            if (File.Exists(manifestFile))
            {
                totalSize += new FileInfo(manifestFile).Length;
                File.Delete(manifestFile);
                itemsRemoved++;
            }

            if (itemsRemoved > 0)
            {
                AnsiConsole.MarkupLine($"[green]  ✓[/] Removed assemblies, latest, and manifest ({FormatBytes(totalSize)})");
            }
            else
            {
                AnsiConsole.MarkupLine("[dim]  No build artifacts to clean[/]");
            }
        }
    }

    private void EnforceLogRetention()
    {
        var logDir = _context.LogDirectory;

        if (!Directory.Exists(logDir))
        {
            return;
        }

        AnsiConsole.MarkupLine("[yellow]Enforcing log retention...[/]");

        var logFiles = Directory.GetFiles(logDir, "build-*.log")
            .Where(f => !f.EndsWith("build-latest.log"))
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.CreationTime)
            .ToList();

        if (logFiles.Count <= BuildContext.MAX_LOG_FILES)
        {
            AnsiConsole.MarkupLine($"[dim]  {logFiles.Count} log files (within {BuildContext.MAX_LOG_FILES} limit)[/]");
            return;
        }

        var filesToDelete = logFiles.Skip(BuildContext.MAX_LOG_FILES).ToList();
        long totalSize = 0;

        foreach (var file in filesToDelete)
        {
            try
            {
                totalSize += file.Length;
                file.Delete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AnsiConsole.MarkupLine($"[dim]  Could not delete {file.FullName.EscapeMarkup()}: {ex.Message.EscapeMarkup()}[/]");
            }
        }

        AnsiConsole.MarkupLine($"[green]  ✓[/] Removed {filesToDelete.Count} old log files ({FormatBytes(totalSize)})");
    }

    private async Task CleanDockerImagesAsync()
    {
        AnsiConsole.MarkupLine("[blue]Cleaning Docker images...[/]");

        var processInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "docker",
            Arguments = "images --filter=reference=atelier-* -q",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(processInfo);
        if (process == null)
        {
            return;
        }

        var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);

        var imageIds = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (imageIds.Length == 0)
        {
            AnsiConsole.MarkupLine("[dim]No Atelier Docker images found[/]");
            return;
        }

        foreach (var imageId in imageIds)
        {
            var removeInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"rmi {imageId.Trim()}",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var removeProcess = System.Diagnostics.Process.Start(removeInfo);
            if (removeProcess != null)
            {
                await removeProcess.WaitForExitAsync().ConfigureAwait(false);
            }
        }

        AnsiConsole.MarkupLine($"[green]Removed {imageIds.Length} Docker image(s)[/]");
    }

    private static long GetDirectorySize(DirectoryInfo directory)
    {
        try
        {
            return directory.GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }
        catch
        {
            return 0;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB"];
        int suffixIndex = 0;
        double size = bytes;

        while (size >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            size /= 1024;
            suffixIndex++;
        }

        return $"{size:F1} {suffixes[suffixIndex]}";
    }
}
