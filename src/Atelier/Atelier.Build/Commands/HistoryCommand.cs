using System.CommandLine;
using Atelier.Build.Commands.Utilities;
using Atelier.Build.Pipeline;
using Spectre.Console;

namespace Atelier.Build.Commands;

public class HistoryCommand : BaseObservabilityCommand
{
    public HistoryCommand() : base("history", "Show build history and timeline")
    {
        var subsystemArgument = new Argument<string?>("subsystem")
        {
            DefaultValueFactory = _ => null,
            Description = "Subsystem name (shows all if omitted)"
        };

        var countOption = new Option<int>("--count", "-c")
        {
            DefaultValueFactory = _ => 10,
            Description = "Number of builds to show (default: 10)"
        };

        var verboseOption = new Option<bool>("--verbose", "-v")
        {
            Description = "Show detailed information"
        };

        var formatOption = CreateFormatOption();

        Add(subsystemArgument);
        Add(countOption);
        Add(verboseOption);
        Add(formatOption);

        this.SetAction(async parseResult =>
        {
            await ExecuteAsync(parseResult.GetValue(subsystemArgument),
                               parseResult.GetValue(countOption),
                               parseResult.GetValue(verboseOption),
                               parseResult.GetValue(formatOption)!).ConfigureAwait(false);
        });
    }

    private async Task ExecuteAsync(string? subsystemName, int count, bool verbose, string format)
    {
        var stateManager = CreateStateManager(verbose);
        var effectiveFormat = ResolveFormat(format);

        if (!string.IsNullOrEmpty(subsystemName))
        {
            await ShowSubsystemHistoryAsync(stateManager, subsystemName, count, verbose, effectiveFormat).ConfigureAwait(false);
        }
        else
        {
            await ShowAllSubsystemsTimelineAsync(stateManager, count, verbose, effectiveFormat).ConfigureAwait(false);
        }
    }

    private async Task ShowSubsystemHistoryAsync(BuildStateManager stateManager, string subsystemName, int count, bool verbose, string format)
    {
        var state = stateManager.GetSubsystemState(subsystemName);

        if (state == null || state.History.Count == 0)
        {
            if (format == "plain" || format == "csv" || format == "json")
            {

                return;
            }
            AnsiConsole.MarkupLine($"[yellow]No build history for {subsystemName}[/]");
            return;
        }

        var historyToShow = state.History.Take(count);

        switch (format)
        {
            case "json":
                OutputJson(historyToShow);
                await Task.CompletedTask.ConfigureAwait(false);
                return;
            case "csv":
                OutputCsv(historyToShow);
                await Task.CompletedTask.ConfigureAwait(false);
                return;
            case "plain":
                OutputPlain(historyToShow);
                await Task.CompletedTask.ConfigureAwait(false);
                return;
        }

        AnsiConsole.Write(new Rule($"[blue]Build History: {subsystemName}[/]").RuleStyle("dim"));
        AnsiConsole.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Timestamp")
            .AddColumn("Duration")
            .AddColumn("Status")
            .AddColumn("Config")
            .AddColumn("Tests")
            .AddColumn("Coverage");

        foreach (var entry in historyToShow)
        {
            var timestamp = entry.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            var duration = $"{entry.Duration:F2}s";
            var status = VisualizationHelper.FormatStatus(entry.Succeeded);
            var config = entry.Configuration;

            var tests = entry.TestResults != null
                ? $"{entry.TestResults.Passed}/{entry.TestResults.Total}"
                : "[dim]N/A[/]";

            var coverage = entry.Coverage != null
                ? $"{entry.Coverage.LineRate:F1}%"
                : "[dim]N/A[/]";

            table.AddRow(timestamp, duration, status, config, tests, coverage);

            if (verbose && entry.TestResults != null)
            {
                var detail = $"[dim]    Tests: {entry.TestResults.Total} total, {entry.TestResults.Failed} failed, {entry.TestResults.Skipped} skipped, {entry.TestResults.Duration:F2}s[/]";
                table.AddRow(string.Empty, string.Empty, string.Empty, string.Empty, detail, string.Empty);
            }
        }

        AnsiConsole.Write(table);

        ShowHistorySummary(state.History);

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private async Task ShowAllSubsystemsTimelineAsync(BuildStateManager stateManager, int count, bool verbose, string format)
    {
        var subsystems = await GetAllSubsystemsAsync(verbose).ConfigureAwait(false);

        var allEntries = new List<(string subsystem, BuildHistoryEntry entry)>();
        foreach (var subsystem in subsystems)
        {
            var state = stateManager.GetSubsystemState(subsystem.Name);
            if (state == null)
            {
                continue;
            }

            foreach (var entry in state.History)
            {
                allEntries.Add((subsystem.Name, entry));
            }
        }

        var timeline = allEntries
            .OrderByDescending(e => e.entry.Timestamp)
            .Take(count)
            .ToList();

        switch (format)
        {
            case "json":
                OutputTimelineJson(timeline);
                return;
            case "csv":
                OutputTimelineCsv(timeline);
                return;
            case "plain":
                OutputTimelinePlain(timeline);
                return;
        }

        AnsiConsole.Write(new Rule("[blue]Build Timeline (All Subsystems)[/]").RuleStyle("dim"));
        AnsiConsole.WriteLine();

        if (timeline.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No build history recorded for any subsystem[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Timestamp")
            .AddColumn("Subsystem")
            .AddColumn("Duration")
            .AddColumn("Status")
            .AddColumn("Config")
            .AddColumn("Tests")
            .AddColumn("Coverage");

        foreach (var (subsystem, entry) in timeline)
        {
            var timestamp = entry.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            var duration = $"{entry.Duration:F2}s";
            var status = VisualizationHelper.FormatStatus(entry.Succeeded);
            var config = entry.Configuration;

            var tests = entry.TestResults != null
                ? $"{entry.TestResults.Passed}/{entry.TestResults.Total}"
                : "[dim]N/A[/]";

            var coverage = entry.Coverage != null
                ? $"{entry.Coverage.LineRate:F1}%"
                : "[dim]N/A[/]";

            table.AddRow(timestamp, subsystem, duration, status, config, tests, coverage);
        }

        AnsiConsole.Write(table);
    }

    private static void OutputTimelineJson(IReadOnlyList<(string subsystem, BuildHistoryEntry entry)> timeline)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(
            timeline.Select(t => new
            {
                subsystem = t.subsystem,
                timestamp = t.entry.Timestamp,
                duration = t.entry.Duration,
                succeeded = t.entry.Succeeded,
                configuration = t.entry.Configuration
            }),
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(json);
    }

    private static void OutputTimelineCsv(IReadOnlyList<(string subsystem, BuildHistoryEntry entry)> timeline)
    {
        Console.WriteLine("Timestamp,Subsystem,Duration,Status,Configuration");
        foreach (var (subsystem, entry) in timeline)
        {
            Console.WriteLine($"{entry.Timestamp:O}," +
                             $"{subsystem}," +
                             $"{entry.Duration:F2}," +
                             $"{(entry.Succeeded ? "Pass" : "Fail")}," +
                             $"{entry.Configuration}");
        }
    }

    private static void OutputTimelinePlain(IReadOnlyList<(string subsystem, BuildHistoryEntry entry)> timeline)
    {
        foreach (var (subsystem, entry) in timeline)
        {
            var timestamp = entry.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            var status = entry.Succeeded ? "Pass" : "Fail";
            Console.WriteLine($"{timestamp}\t{subsystem}\t{entry.Duration:F2}s\t{status}");
        }
    }

    private void ShowHistorySummary(List<BuildHistoryEntry> history)
    {
        if (history.Count == 0)
        {
            return;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[dim]Summary Statistics[/]").RuleStyle("dim"));

        var successRate = (double)history.Count(h => h.Succeeded) / history.Count * 100;
        var avgDuration = history.Average(h => h.Duration);
        var minDuration = history.Min(h => h.Duration);
        var maxDuration = history.Max(h => h.Duration);

        var coverageData = history.Where(h => h.Coverage != null).Select(h => h.Coverage!.LineRate).ToList();
        var avgCoverage = coverageData.Any() ? coverageData.Average() : double.NaN;
        var coverageTrend = coverageData.Count > 1 ? VisualizationHelper.CalculateTrend(coverageData) : 0;

        var grid = new Grid()
            .AddColumn()
            .AddColumn();

        grid.AddRow("Success Rate:", $"[green]{successRate:F1}%[/]");
        grid.AddRow("Avg Duration:", $"{avgDuration:F2}s");
        grid.AddRow("Duration Range:", $"{minDuration:F2}s - {maxDuration:F2}s");

        if (!double.IsNaN(avgCoverage))
        {
            var trendMarkup = VisualizationHelper.GetTrendMarkup(coverageTrend);
            grid.AddRow("Avg Coverage:", $"{avgCoverage:F1}% {trendMarkup}");
        }

        AnsiConsole.Write(grid);
    }

    private void OutputJson(IEnumerable<BuildHistoryEntry> history)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(
            history.Select(h => new
            {
                timestamp = h.Timestamp,
                duration = h.Duration,
                succeeded = h.Succeeded,
                configuration = h.Configuration,
                tests = h.TestResults != null ? new
                {
                    total = h.TestResults.Total,
                    passed = h.TestResults.Passed,
                    failed = h.TestResults.Failed,
                    skipped = h.TestResults.Skipped,
                    duration = h.TestResults.Duration
                } : null,
                coverage = h.Coverage != null ? new
                {
                    lineRate = h.Coverage.LineRate,
                    branchRate = h.Coverage.BranchRate,
                    linesCovered = h.Coverage.LinesCovered,
                    linesTotal = h.Coverage.LinesTotal
                } : null
            }),
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(json);
    }

    private void OutputCsv(IEnumerable<BuildHistoryEntry> history)
    {
        Console.WriteLine("Timestamp,Duration,Status,Configuration,TestsTotal,TestsPassed,TestsFailed,Coverage");
        foreach (var entry in history)
        {
            Console.WriteLine($"{entry.Timestamp:O}," +
                             $"{entry.Duration:F2}," +
                             $"{(entry.Succeeded ? "Pass" : "Fail")}," +
                             $"{entry.Configuration}," +
                             $"{entry.TestResults?.Total ?? 0}," +
                             $"{entry.TestResults?.Passed ?? 0}," +
                             $"{entry.TestResults?.Failed ?? 0}," +
                             $"{entry.Coverage?.LineRate:F1}");
        }
    }

    private void OutputPlain(IEnumerable<BuildHistoryEntry> history)
    {
        foreach (var entry in history)
        {
            var timestamp = entry.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            var status = entry.Succeeded ? "Pass" : "Fail";
            var tests = entry.TestResults != null ? $"{entry.TestResults.Passed}/{entry.TestResults.Total}" : "N/A";
            var coverage = entry.Coverage != null ? $"{entry.Coverage.LineRate:F1}%" : "N/A";

            Console.WriteLine($"{timestamp}\t{entry.Duration:F2}s\t{status}\t{tests}\t{coverage}");
        }
    }
}
