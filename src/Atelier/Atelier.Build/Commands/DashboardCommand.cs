using System.CommandLine;
using Atelier.Build.Commands.Utilities;
using Atelier.Build.Discovery;
using Atelier.Build.Pipeline;
using Spectre.Console;

namespace Atelier.Build.Commands;

public class DashboardCommand : BaseObservabilityCommand
{
    public DashboardCommand() : base("dashboard", "Show real-time build dashboard")
    {
        var refreshOption = new Option<int>("--refresh", "-r")
        {
            DefaultValueFactory = _ => 5,
            Description = "Refresh interval in seconds (default: 5, 0 = no refresh)"
        };

        Add(refreshOption);

        this.SetAction(async parseResult =>
        {
            await ExecuteAsync(parseResult.GetValue(refreshOption)).ConfigureAwait(false);
        });
    }

    private async Task ExecuteAsync(int refreshInterval)
    {
        var interactive = AnsiConsole.Profile.Capabilities.Interactive
            && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));

        if (refreshInterval > 0
            && interactive)
        {
            await RunLiveDashboardAsync(refreshInterval).ConfigureAwait(false);
        }
        else
        {
            await ShowStaticDashboardAsync().ConfigureAwait(false);
        }
    }

    private async Task RunLiveDashboardAsync(int refreshInterval)
    {
        AnsiConsole.Clear();

        using var cancellationSource = new CancellationTokenSource();

        void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            cancellationSource.Cancel();
        }

        Console.CancelKeyPress += OnCancelKeyPress;

        try
        {
            var token = cancellationSource.Token;
            var initial = await CreateDashboardTableAsync().ConfigureAwait(false);

            await AnsiConsole.Live(initial)
                .AutoClear(false)
                .Overflow(VerticalOverflow.Ellipsis)
                .Cropping(VerticalOverflowCropping.Top)
                .StartAsync(async ctx =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        var dashboard = await CreateDashboardTableAsync().ConfigureAwait(false);
                        ctx.UpdateTarget(dashboard);

                        try
                        {
                            await Task.Delay(refreshInterval * 1000, token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }).ConfigureAwait(false);
        }
        finally
        {
            Console.CancelKeyPress -= OnCancelKeyPress;
        }
    }

    private async Task ShowStaticDashboardAsync()
    {
        var dashboard = await CreateDashboardTableAsync().ConfigureAwait(false);
        AnsiConsole.Write(dashboard);
    }

    private async Task<Layout> CreateDashboardTableAsync()
    {
        var stateManager = CreateStateManager();
        var subsystems = await GetAllSubsystemsAsync().ConfigureAwait(false);

        var layout = new Layout("Root")
            .SplitRows(
                new Layout("Header"),
                new Layout("Body"),
                new Layout("Footer"));

        var header = new Panel(
            Align.Center(
                new Markup($"[bold blue]SMASH BUILD DASHBOARD[/]\n[dim]{DateTime.Now:yyyy-MM-dd HH:mm:ss}[/]"),
                VerticalAlignment.Middle))
            .Border(BoxBorder.Double);

        layout["Header"].Update(header);

        layout["Body"].SplitColumns(
            new Layout("Subsystems").Ratio(2),
            new Layout("Stats").Ratio(1));

        var subsystemsTable = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Subsystem")
            .AddColumn("Status")
            .AddColumn("Coverage")
            .AddColumn("Tests")
            .AddColumn("Last Build");

        foreach (var subsystem in subsystems)
        {
            var state = stateManager.GetSubsystemState(subsystem.Name);

            if (state == null)
            {
                subsystemsTable.AddRow(
                    subsystem.Name,
                    "[dim]Not Built[/]",
                    "[dim]--[/]",
                    "[dim]--[/]",
                    "[dim]Never[/]");
                continue;
            }

            var statusMarkup = VisualizationHelper.FormatStatus(state.BuildSucceeded);

            var latestCoverage = state.Coverage;
            var coverageMarkup = latestCoverage != null
                ? $"{latestCoverage.LineRate:F1}%"
                : "[dim]--[/]";

            var testResults = state.TestResults;
            var testsMarkup = testResults != null
                ? $"{testResults.Passed}/{testResults.Total}"
                : "[dim]--[/]";

            var lastBuildMarkup = VisualizationHelper.FormatTimeAgo(state.LastBuildTime);

            var sparkline = string.Empty;
            if (state.History.Count > 0)
            {
                var coverageTrend = state.History
                    .Take(10)
                    .Reverse()
                    .Where(h => h.Coverage != null)
                    .Select(h => h.Coverage!.LineRate)
                    .ToList();

                if (coverageTrend.Any())
                {
                    sparkline = " " + VisualizationHelper.GenerateSparkline(coverageTrend);
                }
            }

            subsystemsTable.AddRow(
                subsystem.Name,
                statusMarkup,
                coverageMarkup + sparkline,
                testsMarkup,
                lastBuildMarkup);
        }

        layout["Subsystems"].Update(
            new Panel(subsystemsTable)
                .Header("[yellow]Subsystems[/]")
                .Border(BoxBorder.Rounded));

        var stats = CreateStatsPanel(stateManager, subsystems);
        layout["Stats"].Update(
            new Panel(stats)
                .Header("[yellow]Statistics[/]")
                .Border(BoxBorder.Rounded));

        var recentActivity = CreateRecentActivityPanel(stateManager, subsystems);
        layout["Footer"].Update(
            new Panel(recentActivity)
                .Header("[yellow]Recent Activity[/]")
                .Border(BoxBorder.Rounded));

        return layout;
    }

    private Markup CreateStatsPanel(BuildStateManager stateManager, IReadOnlyList<SubsystemDefinition> subsystems)
    {
        var allStates = subsystems
            .Select(s => stateManager.GetSubsystemState(s.Name))
            .OfType<SubsystemBuildState>()
            .Where(s => s.History.Count > 0)
            .ToList();

        if (!allStates.Any())
        {
            return new Markup("[dim]No build data available[/]");
        }

        var totalBuilds = allStates.Sum(s => s.History.Count);
        var successfulBuilds = allStates.Sum(s => s.History.Count(h => h.Succeeded));
        var successRate = (double)successfulBuilds / totalBuilds * 100;

        var avgDuration = allStates.Average(s => s.History.Average(h => h.Duration));

        var avgCoverage = allStates
            .SelectMany(s => s.History.Where(h => h.Coverage != null).Select(h => h.Coverage!.LineRate))
            .DefaultIfEmpty(0)
            .Average();

        var totalTests = allStates
            .Where(s => s.TestResults != null)
            .Sum(s => s.TestResults!.Total);

        var passedTests = allStates
            .Where(s => s.TestResults != null)
            .Sum(s => s.TestResults!.Passed);

        var text = $"""
            [bold]Build Statistics[/]

            Total Builds: [cyan]{totalBuilds}[/]
            Success Rate: [green]{successRate:F1}%[/]

            [bold]Performance[/]

            Avg Duration: [cyan]{avgDuration:F2}s[/]

            [bold]Quality[/]

            Avg Coverage: [cyan]{avgCoverage:F1}%[/]
            Total Tests: [cyan]{totalTests}[/]
            Passed: [green]{passedTests}[/]
            """;

        return new Markup(text);
    }

    private Table CreateRecentActivityPanel(BuildStateManager stateManager, IReadOnlyList<SubsystemDefinition> subsystems)
    {
        var allBuilds = new List<(string subsystem, BuildHistoryEntry entry)>();

        foreach (var subsystem in subsystems)
        {
            var state = stateManager.GetSubsystemState(subsystem.Name);
            if (state?.History != null && state.History.Any())
            {
                var latest = state.History.First();
                allBuilds.Add((subsystem.Name, latest));
            }
        }

        var recentBuilds = allBuilds
            .OrderByDescending(b => b.entry.Timestamp)
            .Take(5)
            .ToList();

        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn("Activity");

        foreach (var (subsystem, entry) in recentBuilds)
        {
            var status = VisualizationHelper.FormatStatus(entry.Succeeded);
            var timeAgo = VisualizationHelper.FormatTimeAgo(entry.Timestamp);

            table.AddRow($"{status} [cyan]{subsystem}[/] built [dim]{timeAgo}[/] ([yellow]{entry.Duration:F2}s[/])");
        }

        if (table.Rows.Count == 0)
        {
            table.AddRow("[dim]No recent activity[/]");
        }

        return table;
    }
}
