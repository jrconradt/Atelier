using System.CommandLine;
using System.Diagnostics;
using Atelier.Build.Pipeline;
using Spectre.Console;

namespace Atelier.Build.Commands;

public class ArtifactsCommand : Command
{
    public ArtifactsCommand() : base("artifacts", "Browse generated artifacts")
    {
        var subsystemArgument = new Argument<string>("subsystem")
        {
            Description = "Subsystem name"
        };

        var coverageOption = new Option<bool>("--coverage", "-c")
        {
            Description = "Open coverage report in browser"
        };

        var benchmarksOption = new Option<bool>("--benchmarks", "-b")
        {
            Description = "Show benchmark results"
        };

        var listOption = new Option<bool>("--list", "-l")
        {
            Description = "List all available artifacts"
        };

        Add(subsystemArgument);
        Add(coverageOption);
        Add(benchmarksOption);
        Add(listOption);

        this.SetAction(async parseResult =>
        {
            await ExecuteAsync(parseResult.GetValue(subsystemArgument)!,
                               parseResult.GetValue(coverageOption),
                               parseResult.GetValue(benchmarksOption),
                               parseResult.GetValue(listOption)).ConfigureAwait(false);
        });
    }

    private async Task ExecuteAsync(string subsystemName, bool coverage, bool benchmarks, bool list)
    {
        var workingDirectory = Directory.GetCurrentDirectory();
        var context = new BuildContext
        {
            WorkingDirectory = workingDirectory
        };

        if (list)
        {
            await ListAllArtifactsAsync(context, subsystemName).ConfigureAwait(false);
            return;
        }

        if (coverage)
        {
            await OpenCoverageReportAsync(context, subsystemName).ConfigureAwait(false);
            return;
        }

        if (benchmarks)
        {
            await ShowBenchmarkResultsAsync(context, subsystemName).ConfigureAwait(false);
            return;
        }

        await ListAllArtifactsAsync(context, subsystemName).ConfigureAwait(false);
    }

    private async Task ListAllArtifactsAsync(BuildContext context, string subsystemName)
    {
        AnsiConsole.Write(new Rule($"[blue]Artifacts: {subsystemName}[/]").RuleStyle("dim"));
        AnsiConsole.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Type")
            .AddColumn("Location")
            .AddColumn("Size")
            .AddColumn("Modified");

        var coverageDir = Path.Combine(context.CoverageReportsDirectory, subsystemName, "latest");
        if (Directory.Exists(coverageDir))
        {
            var indexHtml = Path.Combine(coverageDir, "index.html");
            if (File.Exists(indexHtml))
            {
                var fileInfo = new FileInfo(indexHtml);
                table.AddRow(
                    "Coverage Report",
                    indexHtml,
                    FormatFileSize(fileInfo.Length),
                    fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));
            }
        }

        var logDir = context.LogDirectory;
        if (Directory.Exists(logDir))
        {
            var latestLog = Path.Combine(logDir, $"test-{subsystemName}-latest.log");
            if (File.Exists(latestLog))
            {
                var fileInfo = new FileInfo(latestLog);
                table.AddRow(
                    "Test Log",
                    latestLog,
                    FormatFileSize(fileInfo.Length),
                    fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));
            }
        }

        var benchmarkDir = Path.Combine(context.BenchmarkResultsDirectory, subsystemName);
        if (Directory.Exists(benchmarkDir))
        {
            var resultsFiles = Directory.GetFiles(benchmarkDir, "*-report.html", SearchOption.AllDirectories);
            foreach (var file in resultsFiles.Take(3))
            {
                var fileInfo = new FileInfo(file);
                table.AddRow(
                    "Benchmark",
                    file,
                    FormatFileSize(fileInfo.Length),
                    fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));
            }
        }

        if (table.Rows.Count == 0)
        {
            AnsiConsole.MarkupLine($"[yellow]No artifacts found for {subsystemName}[/]");
        }
        else
        {
            AnsiConsole.Write(table);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private async Task OpenCoverageReportAsync(BuildContext context, string subsystemName)
    {
        var coverageDir = Path.Combine(context.CoverageReportsDirectory, subsystemName, "latest");
        var indexHtml = Path.Combine(coverageDir, "index.html");

        if (!File.Exists(indexHtml))
        {
            AnsiConsole.MarkupLine($"[red]Coverage report not found for {subsystemName}[/]");
            AnsiConsole.MarkupLine("[dim]Run: smash {0} -t[/]", subsystemName);
            return;
        }

        var coverageRoot = Path.GetFullPath(context.CoverageReportsDirectory);
        var rootPrefix = coverageRoot.EndsWith(Path.DirectorySeparatorChar)
            ? coverageRoot
            : coverageRoot + Path.DirectorySeparatorChar;
        var resolvedIndexHtml = Path.GetFullPath(indexHtml);

        if (!resolvedIndexHtml.StartsWith(rootPrefix, StringComparison.Ordinal))
        {
            AnsiConsole.MarkupLine($"[red]Refusing to open path outside coverage directory: {resolvedIndexHtml}[/]");
            return;
        }

        AnsiConsole.MarkupLine($"[green]Opening coverage report...[/]");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = resolvedIndexHtml,
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to open browser: {ex.Message}[/]");
            AnsiConsole.MarkupLine($"[dim]File location: {indexHtml}[/]");
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private async Task ShowBenchmarkResultsAsync(BuildContext context, string subsystemName)
    {
        var benchmarkDir = Path.Combine(context.BenchmarkResultsDirectory, subsystemName);

        if (!Directory.Exists(benchmarkDir))
        {
            AnsiConsole.MarkupLine($"[yellow]No benchmark results for {subsystemName}[/]");
            AnsiConsole.MarkupLine("[dim]Run: smash {0} -b[/]", subsystemName);
            return;
        }

        var resultsFiles = Directory.GetFiles(benchmarkDir, "*-report.html", SearchOption.AllDirectories)
            .OrderByDescending(f => File.GetLastWriteTime(f))
            .ToList();

        if (resultsFiles.Count == 0)
        {
            AnsiConsole.MarkupLine($"[yellow]No benchmark reports found[/]");
            return;
        }

        AnsiConsole.Write(new Rule($"[blue]Benchmark Results: {subsystemName}[/]").RuleStyle("dim"));
        AnsiConsole.WriteLine();

        foreach (var file in resultsFiles.Take(5))
        {
            var fileInfo = new FileInfo(file);
            AnsiConsole.MarkupLine($"[green]•[/] {Path.GetFileName(file)}");
            AnsiConsole.MarkupLine($"  [dim]{fileInfo.LastWriteTime:yyyy-MM-dd HH:mm}[/]");
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes}B";
        }
        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024}KB";
        }
        return $"{bytes / (1024 * 1024)}MB";
    }
}
