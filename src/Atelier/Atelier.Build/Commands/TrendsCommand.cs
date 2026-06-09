using System.CommandLine;
using Atelier.Build.Commands.Utilities;
using Atelier.Build.Pipeline;
using Spectre.Console;

namespace Atelier.Build.Commands;

public class TrendsCommand : BaseObservabilityCommand
{
    public TrendsCommand() : base("trends", "Visualize code quality trends")
    {
        var subsystemArgument = new Argument<string>("subsystem")
        {
            Description = "Subsystem name"
        };

        var coverageOption = new Option<bool>("--coverage", "-c")
        {
            Description = "Show coverage trend only"
        };

        var countOption = new Option<int>("--count", "-n")
        {
            DefaultValueFactory = _ => 20,
            Description = "Number of builds to show (default: 20)"
        };

        var formatOption = CreateFormatOption();

        Add(subsystemArgument);
        Add(coverageOption);
        Add(countOption);
        Add(formatOption);

        this.SetAction(async parseResult =>
        {
            await ExecuteAsync(parseResult.GetValue(subsystemArgument)!,
                               parseResult.GetValue(coverageOption),
                               parseResult.GetValue(countOption),
                               parseResult.GetValue(formatOption)!).ConfigureAwait(false);
        });
    }

    private async Task ExecuteAsync(string subsystemName, bool coverageOnly, int count, string format)
    {
        var stateManager = CreateStateManager();
        var effectiveFormat = ResolveFormat(format);
        var state = stateManager.GetSubsystemState(subsystemName);

        if (state == null || state.History.Count == 0)
        {
            if (effectiveFormat == "plain" || effectiveFormat == "csv" || effectiveFormat == "json")
            {

                return;
            }
            AnsiConsole.MarkupLine($"[yellow]No build history for {subsystemName}[/]");
            return;
        }

        var historyToShow = state.History.Take(count).Reverse().ToList();

        switch (effectiveFormat)
        {
            case "json":
                OutputTrendsJson(historyToShow);
                await Task.CompletedTask.ConfigureAwait(false);
                return;
            case "csv":
                OutputTrendsCsv(historyToShow);
                await Task.CompletedTask.ConfigureAwait(false);
                return;
            case "plain":
                OutputTrendsPlain(historyToShow);
                await Task.CompletedTask.ConfigureAwait(false);
                return;
        }

        AnsiConsole.Write(new Rule($"[blue]Quality Trends: {subsystemName}[/]").RuleStyle("dim"));
        AnsiConsole.WriteLine();

        ShowCoverageTrend(historyToShow);
        AnsiConsole.WriteLine();

        if (!coverageOnly)
        {
            ShowDurationTrend(historyToShow);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private void ShowCoverageTrend(List<BuildHistoryEntry> history)
    {
        var coverageData = history
            .Where(h => h.Coverage != null)
            .Select(h => (h.Timestamp, h.Coverage!.LineRate))
            .ToList();

        if (coverageData.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No coverage data available[/]");
            return;
        }

        AnsiConsole.MarkupLine("[yellow]Coverage Trend[/]");

        var chart = new BarChart()
            .Width(60)
            .Label("[bold]Line Coverage %[/]");

        foreach (var (timestamp, lineRate) in coverageData.Take(10))
        {
            var label = timestamp.ToString("MM/dd HH:mm");
            var color = lineRate >= 80 ? Color.Green : lineRate >= 60 ? Color.Yellow : Color.Red;
            chart.AddItem(label, lineRate, color);
        }

        AnsiConsole.Write(chart);

        if (coverageData.Count > 1)
        {
            var values = coverageData.Select(d => d.LineRate).ToList();
            var sparkline = VisualizationHelper.GenerateSparklineWithRange(values);
            var trend = VisualizationHelper.CalculateTrend(values);
            var trendMarkup = VisualizationHelper.GetTrendMarkup(trend);

            AnsiConsole.MarkupLine($"  Trend: {sparkline} {trendMarkup}");
            AnsiConsole.MarkupLine($"  Current: [cyan]{coverageData.Last().LineRate:F1}%[/]");
            AnsiConsole.MarkupLine($"  Average: [dim]{values.Average():F1}%[/]");
        }
    }

    private void ShowDurationTrend(List<BuildHistoryEntry> history)
    {
        if (history.Count == 0)
        {
            return;
        }

        AnsiConsole.MarkupLine("[yellow]Build Duration Trend[/]");

        var durations = history.Select(h => h.Duration).ToList();
        var sparkline = VisualizationHelper.GenerateSparkline(durations);
        var current = durations.Last();
        var avg = durations.Average();
        var min = durations.Min();
        var max = durations.Max();

        AnsiConsole.MarkupLine($"  Trend: {sparkline}");
        AnsiConsole.MarkupLine($"  Current: [cyan]{current:F2}s[/]");
        AnsiConsole.MarkupLine($"  Average: [dim]{avg:F2}s[/]");
        AnsiConsole.MarkupLine($"  Range: [dim]{min:F2}s - {max:F2}s[/]");
    }

    private void OutputTrendsJson(List<BuildHistoryEntry> history)
    {
        var trends = history.Select(h => new
        {
            timestamp = h.Timestamp,
            duration = h.Duration,
            coverage = h.Coverage != null ? new
            {
                lineRate = h.Coverage.LineRate,
                branchRate = h.Coverage.BranchRate
            } : null
        });

        var json = System.Text.Json.JsonSerializer.Serialize(trends,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(json);
    }

    private void OutputTrendsCsv(List<BuildHistoryEntry> history)
    {
        Console.WriteLine("Timestamp,Duration,CoverageLineRate,CoverageBranchRate");

        foreach (var entry in history)
        {
            Console.WriteLine($"{entry.Timestamp:O}," +
                             $"{entry.Duration:F2}," +
                             $"{entry.Coverage?.LineRate.ToString("F1") ?? ""}," +
                             $"{entry.Coverage?.BranchRate.ToString("F1") ?? ""}");
        }
    }

    private void OutputTrendsPlain(List<BuildHistoryEntry> history)
    {
        foreach (var entry in history)
        {
            var parts = new List<string>
            {
                entry.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                $"{entry.Duration:F2}s"
            };

            if (entry.Coverage != null)
            {
                parts.Add($"Cov:{entry.Coverage.LineRate:F1}%");
            }

            Console.WriteLine(string.Join("\t", parts));
        }
    }
}
