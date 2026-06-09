using System.Collections.Concurrent;
using System.Diagnostics;
using Atelier.Build.Discovery;
using Atelier.Build.Pipeline;
using Atelier.Build.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using T = Atelier.Build.Templates;

namespace Atelier.Build.Services;

public class BoutiqueCompilationService : IBoutiqueCompilationService
{
    private readonly ILogger<BoutiqueCompilationService> _logger;
    private readonly string _logDirectory;
    private readonly ConcurrentDictionary<string, Lazy<Task>> _builtTargets = new(StringComparer.OrdinalIgnoreCase);

    public BoutiqueCompilationService(
        string logDirectory,
        ILogger<BoutiqueCompilationService>? logger = null)
    {
        _logDirectory = logDirectory;
        _logger = logger ?? NullLogger<BoutiqueCompilationService>.Instance;
    }

    public async Task<BoutiqueManifest> CompileBoutiqueAsync(
        BoutiqueDefinition definition,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);

        await EnsureSolutionBuiltAsync(definition, outputDirectory, cancellationToken).ConfigureAwait(false);

        var assemblyName = Path.GetFileNameWithoutExtension(definition.ProjectPath);
        var outputAssembly = Path.Combine(outputDirectory, $"{assemblyName}.dll");

        if (!File.Exists(outputAssembly))
        {
            throw new InvalidOperationException(
                $"Expected assembly not found after build: {outputAssembly}");
        }

        return new BoutiqueManifest
        {
            Name = definition.Name,
            ProjectPath = definition.ProjectPath,
            OutputAssembly = outputAssembly,
            Offerings = definition.Offerings.Select(o => o.Name).ToList(),
            Dependencies = definition.Dependencies.ToList()
        };
    }

    public async Task<bool> CompileSolutionAsync(
        string solutionPath,
        string outputDirectory,
        string configuration = "Debug",
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Building solution: {SolutionPath}", solutionPath);

        if (!TryValidateConfiguration(configuration))
        {
            return false;
        }

        var args = new List<string>
        {
            "build",
            solutionPath,
            "-c",
            configuration,
            "-o",
            outputDirectory,
            "--no-incremental"
        };

        return await RunBuildAsync(
            args,
            Path.GetDirectoryName(solutionPath)!,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> CompileProjectAsync(
        string projectPath,
        string outputDirectory,
        string configuration = "Debug",
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Building project: {ProjectPath}", projectPath);

        if (!TryValidateConfiguration(configuration))
        {
            return false;
        }

        var args = new List<string>
        {
            "build",
            projectPath,
            "-c",
            configuration,
            "-o",
            outputDirectory
        };

        return await RunBuildAsync(
            args,
            Path.GetDirectoryName(projectPath)!,
            cancellationToken).ConfigureAwait(false);
    }

    private bool TryValidateConfiguration(string configuration)
    {
        if (configuration == "Debug" || configuration == "Release")
        {
            return true;
        }

        _logger.LogError(
            "Invalid build configuration '{Configuration}'; expected 'Debug' or 'Release'",
            configuration);
        return false;
    }

    public void ResetBuildCache()
    {
        _builtTargets.Clear();
    }

    private async Task EnsureSolutionBuiltAsync(
        BoutiqueDefinition definition,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var solutionPath = FindSolutionFile(Path.GetDirectoryName(definition.ProjectPath)!);
        var target = solutionPath ?? definition.ProjectPath;
        var sourceSignature = ComputeSourceSignature(Path.GetDirectoryName(target)!);
        var cacheKey = $"{target}|{definition.Build.Configuration}|{outputDirectory}|{sourceSignature}";

        var build = _builtTargets.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task>(() => BuildTargetAsync(
                solutionPath,
                definition,
                outputDirectory,
                cancellationToken)));

        await build.Value.ConfigureAwait(false);
    }

    private async Task BuildTargetAsync(
        string? solutionPath,
        BoutiqueDefinition definition,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        if (solutionPath != null)
        {
            await CompileSolutionAsync(
                solutionPath,
                outputDirectory,
                definition.Build.Configuration,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await CompileProjectAsync(
                definition.ProjectPath,
                outputDirectory,
                definition.Build.Configuration,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static long ComputeSourceSignature(string directory)
    {
        if (string.IsNullOrEmpty(directory)
            || !Directory.Exists(directory))
        {
            return 0;
        }

        var patterns = new[] { "*.cs", "*.csproj", "*.sln", "smash.yml", "*.proto" };
        long latest = 0;

        foreach (var pattern in patterns)
        {
            foreach (var file in Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories))
            {
                if (Atelier.Build.Utils.PathSegments.IsUnderBinOrObj(file))
                {
                    continue;
                }

                var ticks = File.GetLastWriteTimeUtc(file).Ticks;
                if (ticks > latest)
                {
                    latest = ticks;
                }
            }
        }

        return latest;
    }

    private static string? FindSolutionFile(string startDirectory)
    {
        var currentDir = startDirectory;

        while (!string.IsNullOrEmpty(currentDir))
        {
            var slnFiles = Directory.GetFiles(currentDir, "*.sln");
            if (slnFiles.Length > 0)
            {
                return slnFiles[0];
            }

            currentDir = Path.GetDirectoryName(currentDir);
        }

        return null;
    }

    private async Task<bool> RunBuildAsync(
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            processInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(processInfo)
            ?? throw new InvalidOperationException("Failed to start dotnet build process");

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);

        var combinedOutput = $"{output}\n{error}".Trim();

        await WriteBuildLogAsync(combinedOutput, process.ExitCode, cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            _logger.LogError("Build failed with exit code {ExitCode}", process.ExitCode);
            LogTruncatedErrors(combinedOutput);
            return false;
        }

        _logger.LogInformation("Build succeeded");
        return true;
    }

    private void LogTruncatedErrors(string output)
    {
        var lines = output.Split('\n');
        var errorLines = lines
            .Where(l => l.Contains(": error ", StringComparison.OrdinalIgnoreCase))
            .Take(5)
            .ToList();

        foreach (var errorLine in errorLines)
        {
            _logger.LogError("{ErrorLine}", errorLine.Trim());
        }

        var totalErrors = lines.Count(l => l.Contains(": error ", StringComparison.OrdinalIgnoreCase));
        if (totalErrors > 5)
        {
            _logger.LogWarning("... and {RemainingErrors} more error(s)", totalErrors - 5);
        }
    }

    private async Task WriteBuildLogAsync(
        string output,
        int exitCode,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_logDirectory);

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var logFileName = $"build-{timestamp}.log";
        var logPath = Path.Combine(_logDirectory, logFileName);

        var logContent = new T.BuildLog
        {
            Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ExitCode = exitCode,
            Status = exitCode == 0 ? "SUCCESS" : "FAILED",
            Output = output,
        }.Render();

        await File.WriteAllTextAsync(logPath, logContent, cancellationToken).ConfigureAwait(false);

        var latestLogPath = Path.Combine(_logDirectory, "build-latest.log");
        await File.WriteAllTextAsync(latestLogPath, logContent, cancellationToken).ConfigureAwait(false);

        EnforceLogRetention();

        _logger.LogDebug("Build log written to: {LogPath}", logPath);
    }

    private void EnforceLogRetention()
    {
        const int MAX_LOG_FILES = 5;

        var logFiles = Directory.GetFiles(_logDirectory, "build-*.log")
            .Where(f => !f.EndsWith("build-latest.log"))
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.CreationTime)
            .ToList();

        if (logFiles.Count > MAX_LOG_FILES)
        {
            foreach (var oldLog in logFiles.Skip(MAX_LOG_FILES))
            {
                try
                {
                    oldLog.Delete();
                    _logger.LogDebug("Deleted old log: {LogName}", oldLog.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete old log: {LogName}", oldLog.Name);
                }
            }
        }
    }
}
