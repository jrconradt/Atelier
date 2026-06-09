using System.Diagnostics;
using System.Globalization;
using System.Xml.Linq;
using Atelier.Build.Discovery;
using Atelier.Build.Utils;
using Spectre.Console;

namespace Atelier.Build.Pipeline;

public class CoverageCollector
{
    private readonly BuildContext _context;

    public CoverageCollector(BuildContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

        public IReadOnlyList<string> GenerateCoverageArguments(
        string subsystemName,
        string projectName,
        CoverageConfig? config,
        out string coverageFilePath)
    {
        config ??= new CoverageConfig();

        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss-fffffff");
        var coverageDir = Path.Combine(
            _context.CoverageOutputDirectory,
            subsystemName,
            projectName,
            timestamp);

        Directory.CreateDirectory(coverageDir);

        var formats = string.Join(",", config.Formats);
        var coveragePathPrefix = Path.Combine(coverageDir, "coverage").Replace("\\", "/");

        var parts = new List<string>
        {
            "/p:CollectCoverage=true",
            $"/p:CoverletOutputFormat={formats}",
            $"/p:CoverletOutput={coveragePathPrefix}"
        };

        if (config.Exclude.Count > 0)
        {
            parts.Add($"/p:Exclude={string.Join(",", config.Exclude)}");
        }

        if (config.Include is { Count: > 0 })
        {
            parts.Add($"/p:Include={string.Join(",", config.Include)}");
        }

        coverageFilePath = $"{coveragePathPrefix}.cobertura.xml";

        return parts;
    }

        public CoverageSummary? ParseCoverageSummary(string coverageDirectory)
    {
        var coberturaFiles = Directory.GetFiles(coverageDirectory, "coverage.cobertura.xml", SearchOption.AllDirectories);
        if (coberturaFiles.Length == 0)
        {
            if (_context.Verbose)
            {
                AnsiConsole.MarkupLine("[yellow]  Warning: No coverage.cobertura.xml found[/]");
            }
            return null;
        }

        var totals = ReadCoberturaTotals(coberturaFiles[0]);
        if (totals is null)
        {
            if (_context.Verbose)
            {
                AnsiConsole.MarkupLine("[yellow]  Warning: Could not parse coverage file[/]");
            }
            return null;
        }

        return new CoverageSummary
        {
            LineRate = totals.LineRate,
            BranchRate = totals.BranchRate,
            LinesCovered = totals.LinesCovered,
            LinesValid = totals.LinesValid,
            BranchesCovered = totals.BranchesCovered,
            BranchesValid = totals.BranchesValid,
            CoverageDirectory = coverageDirectory
        };
    }

        public static CoberturaTotals? ReadCoberturaTotals(string coberturaFile)
    {
        if (!File.Exists(coberturaFile))
        {
            return null;
        }

        try
        {
            var coverage = XDocument.Load(coberturaFile).Root;
            if (coverage is null)
            {
                return null;
            }

            return new CoberturaTotals(
                LineRate: ParseDouble(coverage.Attribute("line-rate")?.Value),
                BranchRate: ParseDouble(coverage.Attribute("branch-rate")?.Value),
                LinesCovered: ParseInt(coverage.Attribute("lines-covered")?.Value),
                LinesValid: ParseInt(coverage.Attribute("lines-valid")?.Value),
                BranchesCovered: ParseInt(coverage.Attribute("branches-covered")?.Value),
                BranchesValid: ParseInt(coverage.Attribute("branches-valid")?.Value));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: failed to parse coverage file {coberturaFile}: {ex.Message}");
            return null;
        }
    }

    private static double ParseDouble(string? value)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }

    private static int ParseInt(string? value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }

        public async Task<string?> GenerateHtmlReportAsync(string subsystemName, string coverageDirectory)
    {
        try
        {

            var coverageFiles = Directory.GetFiles(coverageDirectory, "coverage.cobertura.xml", SearchOption.AllDirectories);

            if (coverageFiles.Length == 0)
            {
                return null;
            }

            var reportDir = Path.Combine(_context.CoverageReportsDirectory, subsystemName, "latest");
            Directory.CreateDirectory(reportDir);

            var reportsArg = string.Join(";", coverageFiles);
            var args = new List<string>
            {
                $"-reports:{reportsArg}",
                $"-targetdir:{reportDir}",
                "-reporttypes:Html"
            };

            var executor = new ProcessExecutor(_context);
            try
            {
                var result = await executor.ExecuteAsync(
                    "reportgenerator",
                    args,
                    Directory.GetCurrentDirectory(),
                    ProcessOptions.WithTimeout(_context.Timeouts.CoverageReport),
                    CancellationToken.None).ConfigureAwait(false);

                if (result.Success)
                {
                    return Path.Combine(reportDir, "index.html");
                }
                else
                {
                    if (_context.Verbose)
                    {
                        AnsiConsole.MarkupLine($"[yellow]  Warning: reportgenerator failed: {result.StandardError}[/]");
                    }
                    return null;
                }
            }
            catch (ProcessExecutionException ex)
            {
                if (_context.Verbose)
                {
                    AnsiConsole.MarkupLine($"[yellow]  Warning: Could not start reportgenerator: {ex.Message}[/]");
                }
                return null;
            }
        }
        catch (Exception ex)
        {
            if (_context.Verbose)
            {
                AnsiConsole.MarkupLine($"[yellow]  Warning: reportgenerator not found or failed: {ex.Message}[/]");
                AnsiConsole.MarkupLine("[dim]    Install: dotnet tool install -g dotnet-reportgenerator-globaltool[/]");
            }
            return null;
        }
    }

        public void DisplayCoverageSummary(string projectName, CoverageSummary summary, CoverageConfig? config)
    {
        config ??= new CoverageConfig();

        var linePercentage = summary.LineRate * 100;
        var branchPercentage = summary.BranchRate * 100;

        var lineColor = GetCoverageColor(linePercentage, config.Threshold);
        var branchColor = GetCoverageColor(branchPercentage, config.Threshold);

        AnsiConsole.MarkupLine($"  [{lineColor}]Coverage: {linePercentage:F1}% ({summary.LinesCovered}/{summary.LinesValid} lines)[/]");
        AnsiConsole.MarkupLine($"  [{branchColor}]Branches: {branchPercentage:F1}% ({summary.BranchesCovered}/{summary.BranchesValid} branches)[/]");

        if (linePercentage < config.Threshold)
        {
            AnsiConsole.MarkupLine($"  [yellow]⚠ Coverage below threshold ({config.Threshold}%)[/]");
        }
    }

    private static string GetCoverageColor(double percentage, int threshold)
    {
        if (percentage >= threshold)
        {
            return "green";
        }
        else if (percentage >= threshold - 20)
        {
            return "yellow";
        }
        else
        {
            return "red";
        }
    }
}

public class CoverageSummary
{
    public double LineRate { get; init; }
    public double BranchRate { get; init; }
    public int LinesCovered { get; init; }
    public int LinesValid { get; init; }
    public int BranchesCovered { get; init; }
    public int BranchesValid { get; init; }
    public string CoverageDirectory { get; init; } = string.Empty;
}

public record CoberturaTotals(
    double LineRate,
    double BranchRate,
    int LinesCovered,
    int LinesValid,
    int BranchesCovered,
    int BranchesValid);
