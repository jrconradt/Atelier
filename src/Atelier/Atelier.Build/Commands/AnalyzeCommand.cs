using System.CommandLine;
using Atelier.Build.Commands.Utilities;
using Atelier.Build.Discovery;
using Atelier.Build.Pipeline;
using Spectre.Console;

namespace Atelier.Build.Commands;

public class AnalyzeCommand : BaseObservabilityCommand
{
    public AnalyzeCommand() : base("analyze", "Analyze build health across subsystems")
    {
        var slowestOption = new Option<bool>("--slowest", "-s")
        {
            Description = "Show slowest builds and tests"
        };

        var failuresOption = new Option<bool>("--failures", "-f")
        {
            Description = "Show failure rates by subsystem"
        };

        var flakyOption = new Option<bool>("--flaky")
        {
            Description = "Detect flaky tests"
        };

        var formatOption = CreateFormatOption();

        Add(slowestOption);
        Add(failuresOption);
        Add(flakyOption);
        Add(formatOption);

        this.SetAction(async parseResult =>
        {
            await ExecuteAsync(parseResult.GetValue(slowestOption),
                               parseResult.GetValue(failuresOption),
                               parseResult.GetValue(flakyOption),
                               parseResult.GetValue(formatOption)!).ConfigureAwait(false);
        });
    }

    private async Task ExecuteAsync(bool showSlowest, bool showFailures, bool showFlaky, string format)
    {
        var stateManager = CreateStateManager();
        var subsystems = await GetAllSubsystemsAsync().ConfigureAwait(false);
        var effectiveFormat = ResolveFormat(format);

        if (subsystems.Count == 0)
        {
            if (effectiveFormat == "plain" || effectiveFormat == "csv"
                || effectiveFormat == "json")
            {

                return;
            }
            AnsiConsole.MarkupLine("[yellow]No subsystems found[/]");
            return;
        }

        if (!showSlowest && !showFailures
            && !showFlaky)
        {
            await ShowHealthDashboardAsync(stateManager, subsystems, effectiveFormat).ConfigureAwait(false);
            return;
        }

        if (showSlowest)
        {
            await ShowSlowestBuildsAsync(stateManager, subsystems).ConfigureAwait(false);
        }

        if (showFailures)
        {
            await ShowFailureRatesAsync(stateManager, subsystems).ConfigureAwait(false);
        }

        if (showFlaky)
        {
            await ShowFlakyTestsAsync(stateManager, subsystems).ConfigureAwait(false);
        }
    }

    private async Task ShowHealthDashboardAsync(BuildStateManager stateManager, IReadOnlyList<SubsystemDefinition> subsystems, string format)
    {

        var healthData = subsystems.Select(subsystem =>
        {
            var state = stateManager.GetSubsystemState(subsystem.Name);
            if (state == null || state.History.Count == 0)
            {
                return new
                {
                    subsystem = subsystem.Name,
                    successRate = (double?)null,
                    avgDuration = (double?)null,
                    coverage = (double?)null,
                    lastBuild = (DateTime?)null
                };
            }

            var successRate = (double)state.History.Count(h => h.Succeeded) / state.History.Count * 100;
            var avgDuration = state.History.Average(h => h.Duration);
            var latestCoverage = state.History.FirstOrDefault(h => h.Coverage != null)?.Coverage;

            return new
            {
                subsystem = subsystem.Name,
                successRate = (double?)successRate,
                avgDuration = (double?)avgDuration,
                coverage = latestCoverage?.LineRate,
                lastBuild = (DateTime?)state.LastBuildTime
            };
        }).ToList();

        switch (format)
        {
            case "json":
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(healthData,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                await Task.CompletedTask.ConfigureAwait(false);
                return;
            case "csv":
                Console.WriteLine("Subsystem,SuccessRate,AvgDuration,Coverage,LastBuild");
                foreach (var data in healthData)
                {
                    Console.WriteLine($"{data.subsystem}," +
                                     $"{data.successRate?.ToString("F1") ?? ""}," +
                                     $"{data.avgDuration?.ToString("F2") ?? ""}," +
                                     $"{data.coverage?.ToString("F1") ?? ""}," +
                                     $"{data.lastBuild?.ToString("O") ?? ""}");
                }
                await Task.CompletedTask.ConfigureAwait(false);
                return;
            case "plain":
                foreach (var data in healthData)
                {
                    Console.WriteLine($"{data.subsystem}\t" +
                                     $"{data.successRate?.ToString("F1") ?? "N/A"}%\t" +
                                     $"{data.avgDuration?.ToString("F2") ?? "N/A"}s\t" +
                                     $"{data.coverage?.ToString("F1") ?? "N/A"}%\t" +
                                     $"{(data.lastBuild.HasValue ? VisualizationHelper.FormatTimeAgo(data.lastBuild.Value) : "Never")}");
                }
                await Task.CompletedTask.ConfigureAwait(false);
                return;
        }

        AnsiConsole.Write(new Rule("[blue]Build Health Dashboard[/]").RuleStyle("dim"));
        AnsiConsole.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Subsystem")
            .AddColumn("Success Rate")
            .AddColumn("Avg Duration")
            .AddColumn("Coverage")
            .AddColumn("Last Build");

        foreach (var subsystem in subsystems)
        {
            var state = stateManager.GetSubsystemState(subsystem.Name);

            if (state == null || state.History.Count == 0)
            {
                table.AddRow(
                    subsystem.Name,
                    "[dim]N/A[/]",
                    "[dim]N/A[/]",
                    "[dim]N/A[/]",
                    "[dim]Never[/]");
                continue;
            }

            var successRate = (double)state.History.Count(h => h.Succeeded) / state.History.Count * 100;
            var successMarkup = VisualizationHelper.FormatPercentage(successRate, 90, 70);

            var avgDuration = state.History.Average(h => h.Duration);

            var latestCoverage = state.History.FirstOrDefault(h => h.Coverage != null)?.Coverage;
            var coverageMarkup = latestCoverage != null ? $"{latestCoverage.LineRate:F1}%" : "[dim]N/A[/]";

            var lastBuildMarkup = VisualizationHelper.FormatTimeAgo(state.LastBuildTime);

            table.AddRow(
                subsystem.Name,
                successMarkup,
                $"{avgDuration:F2}s",
                coverageMarkup,
                lastBuildMarkup);
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        var allStates = subsystems
            .Select(s => stateManager.GetSubsystemState(s.Name))
            .OfType<SubsystemBuildState>()
            .Where(s => s.History.Count > 0)
            .ToList();

        if (allStates.Any())
        {
            var overallSuccessRate = allStates.Average(s => (double)s.History.Count(h => h.Succeeded) / s.History.Count * 100);
            var totalBuilds = allStates.Sum(s => s.History.Count);
            var avgBuildTime = allStates.Average(s => s.History.Average(h => h.Duration));

            var summary = new Panel(
                $"Overall Success Rate: [cyan]{overallSuccessRate:F1}%[/] | " +
                $"Total Builds: [cyan]{totalBuilds}[/] | " +
                $"Avg Build Time: [cyan]{avgBuildTime:F2}s[/]")
                .Border(BoxBorder.Rounded)
                .Header("[yellow]Summary[/]");

            AnsiConsole.Write(summary);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private async Task ShowSlowestBuildsAsync(BuildStateManager stateManager, IReadOnlyList<SubsystemDefinition> subsystems)
    {
        AnsiConsole.Write(new Rule("[blue]Slowest Builds & Tests[/]").RuleStyle("dim"));
        AnsiConsole.WriteLine();

        var allBuilds = new List<(string subsystem, BuildHistoryEntry entry)>();

        foreach (var subsystem in subsystems)
        {
            var state = stateManager.GetSubsystemState(subsystem.Name);
            if (state?.History != null)
            {
                foreach (var entry in state.History)
                {
                    allBuilds.Add((subsystem.Name, entry));
                }
            }
        }

        var slowestBuilds = allBuilds
            .OrderByDescending(b => b.entry.Duration)
            .Take(10)
            .ToList();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Rank")
            .AddColumn("Subsystem")
            .AddColumn("Duration")
            .AddColumn("Timestamp")
            .AddColumn("Status");

        var rank = 1;
        foreach (var (subsystem, entry) in slowestBuilds)
        {
            var status = VisualizationHelper.FormatStatus(entry.Succeeded);
            table.AddRow(
                $"{rank}",
                subsystem,
                VisualizationHelper.FormatDuration(entry.Duration),
                entry.Timestamp.ToLocalTime().ToString("MM/dd HH:mm"),
                status);
            rank++;
        }

        AnsiConsole.Write(table);

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private async Task ShowFailureRatesAsync(BuildStateManager stateManager, IReadOnlyList<SubsystemDefinition> subsystems)
    {
        AnsiConsole.Write(new Rule("[blue]Failure Rates by Subsystem[/]").RuleStyle("dim"));
        AnsiConsole.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Subsystem")
            .AddColumn("Total Builds")
            .AddColumn("Failures")
            .AddColumn("Failure Rate")
            .AddColumn("Last Failure");

        foreach (var subsystem in subsystems)
        {
            var state = stateManager.GetSubsystemState(subsystem.Name);

            if (state == null || state.History.Count == 0)
            {
                continue;
            }

            var totalBuilds = state.History.Count;
            var failures = state.History.Count(h => !h.Succeeded);
            var failureRate = (double)failures / totalBuilds * 100;

            if (failures == 0)
            {
                continue;
            }

            var failureRateMarkup = failureRate > 20 ? $"[red]{failureRate:F1}% (high)[/]" :
                                    failureRate > 10 ? $"[yellow]{failureRate:F1}% (elevated)[/]" :
                                    $"[green]{failureRate:F1}% (ok)[/]";

            var lastFailure = state.History.FirstOrDefault(h => !h.Succeeded);
            var lastFailureMarkup = lastFailure != null ?
                lastFailure.Timestamp.ToLocalTime().ToString("MM/dd HH:mm") :
                "[dim]None[/]";

            table.AddRow(
                subsystem.Name,
                totalBuilds.ToString(),
                failures.ToString(),
                failureRateMarkup,
                lastFailureMarkup);
        }

        if (table.Rows.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]No failures detected! 🎉[/]");
        }
        else
        {
            AnsiConsole.Write(table);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private async Task ShowFlakyTestsAsync(BuildStateManager stateManager, IReadOnlyList<SubsystemDefinition> subsystems)
    {
        AnsiConsole.Write(new Rule("[blue]Flaky Test Detection[/]").RuleStyle("dim"));
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[yellow]Analyzing test stability patterns...[/]");
        AnsiConsole.WriteLine();

        var flakyDetected = false;

        foreach (var subsystem in subsystems)
        {
            var state = stateManager.GetSubsystemState(subsystem.Name);

            if (state == null || state.History.Count < 5)
            {
                continue;
            }

            var recentResults = state.History.Take(10).ToList();
            var pattern = string.Join(string.Empty, recentResults.Select(h =>
                h.TestResults?.Failed > 0 ? "F" :
                h.TestResults?.Passed > 0 ? "P" : "-"));

            var alternations = 0;
            for (int i = 1; i < pattern.Length; i++)
            {
                if (pattern[i] != pattern[i - 1] && pattern[i] != '-'
                    && pattern[i - 1] != '-')
                {
                    alternations++;
                }
            }

            if (alternations >= 3)
            {
                flakyDetected = true;
                AnsiConsole.MarkupLine($"[red]⚠ {subsystem.Name}[/]: Unstable test pattern detected");
                AnsiConsole.MarkupLine($"  [dim]Pattern (latest first): {pattern}[/]");
                AnsiConsole.MarkupLine($"  [dim]Alternations: {alternations}[/]");
                AnsiConsole.WriteLine();
            }
        }

        if (!flakyDetected)
        {
            AnsiConsole.MarkupLine("[green]No flaky tests detected! All tests are stable.[/]");
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
