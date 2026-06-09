using System.Diagnostics;
using Atelier.Build.Pipeline;
using Spectre.Console;

namespace Atelier.Build.Discovery;

public class BoutiqueCompiler
{
    private readonly BuildContext _context;

    public BoutiqueCompiler(BuildContext context)
    {
        _context = context;
    }

    public async Task<BoutiqueManifest> CompileAsync(BoutiqueDefinition definition)
    {
        var sharedOutputDir = Path.Combine(_context.BuildOutputDirectory, "assemblies");
        Directory.CreateDirectory(sharedOutputDir);

        await EnsureSolutionBuiltAsync(definition, sharedOutputDir).ConfigureAwait(false);

        var assemblyName = Path.GetFileNameWithoutExtension(definition.ProjectPath);
        var outputAssembly = Path.Combine(sharedOutputDir, $"{assemblyName}.dll");

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

    private async Task EnsureSolutionBuiltAsync(BoutiqueDefinition definition, string outputDir)
    {


        await BuildProjectAsync(definition, outputDir).ConfigureAwait(false);
    }

    private async Task BuildProjectAsync(BoutiqueDefinition definition, string outputDir)
    {
        var args = new List<string>
        {
            "build",
            definition.ProjectPath,
            "-c",
            ValidateConfiguration(definition.Build.Configuration),
            "-o",
            outputDir
        };

        if (definition.Build.TreatWarningsAsErrors)
        {
            args.Add("-warnaserror");
        }

        foreach (var additionalArg in definition.Build.AdditionalMsBuildArgs)
        {
            ValidateMsBuildArg(additionalArg);
            args.Add(additionalArg);
        }

        await RunBuildAsync(args, Path.GetDirectoryName(definition.ProjectPath)!).ConfigureAwait(false);
    }

    private static string ValidateConfiguration(string configuration)
    {
        if (configuration == "Debug" || configuration == "Release")
        {
            return configuration;
        }

        throw new InvalidOperationException(
            $"Invalid build configuration '{configuration}'; expected 'Debug' or 'Release'");
    }

    private static readonly string[] ForbiddenMsBuildArgPrefixes =
    {
        "-t:",
        "/t:",
        "-target",
        "/target"
    };

    private static readonly string[] ForbiddenMsBuildArgFragments =
    {
        "prebuildevent",
        "postbuildevent",
        "exec"
    };

    private static void ValidateMsBuildArg(string argument)
    {
        if (argument.StartsWith('@'))
        {
            throw new InvalidOperationException(
                $"INVALID_MSBUILD_ARG: response file arguments are not permitted: '{argument}'");
        }

        var normalized = argument.ToLowerInvariant();

        foreach (var prefix in ForbiddenMsBuildArgPrefixes)
        {
            if (normalized.StartsWith(prefix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"INVALID_MSBUILD_ARG: target-selection arguments are not permitted: '{argument}'");
            }
        }

        foreach (var fragment in ForbiddenMsBuildArgFragments)
        {
            if (normalized.Contains(fragment, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"INVALID_MSBUILD_ARG: build-event/exec injection arguments are not permitted: '{argument}'");
            }
        }
    }

    private async Task RunBuildAsync(IReadOnlyList<string> arguments, string workingDirectory)
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

        var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        var error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);

        await process.WaitForExitAsync().ConfigureAwait(false);

        var combinedOutput = $"{output}\n{error}".Trim();

        var logPath = await WriteBuildLogAsync(combinedOutput, process.ExitCode).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            DisplayTruncatedErrors(combinedOutput, logPath);
            throw new InvalidOperationException("Build failed");
        }

        if (_context.Verbose && !string.IsNullOrWhiteSpace(output))
        {
            AnsiConsole.WriteLine(output);
        }
    }

    private static void DisplayTruncatedErrors(string output, string logPath)
    {
        var lines = output.Split('\n');
        var errorLines = lines
            .Where(l => l.Contains(": error ", StringComparison.OrdinalIgnoreCase))
            .Take(5)
            .ToList();

        if (errorLines.Count > 0)
        {
            AnsiConsole.MarkupLine("[red]Build errors:[/]");
            foreach (var errorLine in errorLines)
            {
                AnsiConsole.WriteLine($"  {errorLine.Trim()}");
            }

            var totalErrors = lines.Count(l => l.Contains(": error ", StringComparison.OrdinalIgnoreCase));
            if (totalErrors > 5)
            {
                AnsiConsole.MarkupLine($"[yellow]  ... and {totalErrors - 5} more error(s)[/]");
            }

            AnsiConsole.MarkupLine($"[dim]Full output: {logPath}[/]");
        }
        else
        {
            AnsiConsole.WriteLine(output);
        }
    }

    private async Task<string> WriteBuildLogAsync(string output, int exitCode)
    {
        Directory.CreateDirectory(_context.LogDirectory);

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var logFileName = $"build-{timestamp}.log";
        var logPath = Path.Combine(_context.LogDirectory, logFileName);

        var logContent = $"""
            ╔══════════════════════════════════════════════════════════════╗
            ║  SMASH BUILD LOG                                             ║
            ╠══════════════════════════════════════════════════════════════╣
            ║  Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
            ║  Exit Code: {exitCode}
            ║  Status: {(exitCode == 0 ? "SUCCESS" : "FAILED")}
            ╚══════════════════════════════════════════════════════════════╝

            {output}
            """;

        await File.WriteAllTextAsync(logPath, logContent).ConfigureAwait(false);

        var latestLogPath = Path.Combine(_context.LogDirectory, "build-latest.log");
        await File.WriteAllTextAsync(latestLogPath, logContent).ConfigureAwait(false);

        EnforceLogRetention();

        if (_context.Verbose)
        {
            AnsiConsole.MarkupLine($"  [dim]Log written to: {logPath}[/]");
        }

        return logPath;
    }

    private void EnforceLogRetention()
    {
        var logFiles = Directory.GetFiles(_context.LogDirectory, "build-*.log")
            .Where(f => !f.EndsWith("build-latest.log"))
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.CreationTime)
            .ToList();

        if (logFiles.Count > BuildContext.MAX_LOG_FILES)
        {
            foreach (var oldLog in logFiles.Skip(BuildContext.MAX_LOG_FILES))
            {
                try
                {
                    oldLog.Delete();
                    if (_context.Verbose)
                    {
                        AnsiConsole.MarkupLine($"  [dim]Deleted old log: {oldLog.Name}[/]");
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (_context.Verbose)
                    {
                        AnsiConsole.MarkupLine($"  [dim]Could not delete old log {oldLog.Name.EscapeMarkup()}: {ex.Message.EscapeMarkup()}[/]");
                    }
                }
            }
        }
    }

}
