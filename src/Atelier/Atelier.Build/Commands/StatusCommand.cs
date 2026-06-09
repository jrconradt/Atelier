using System.CommandLine;
using Atelier.Build.Discovery;
using Atelier.Build.Pipeline;
using Spectre.Console;

namespace Atelier.Build.Commands;

public class StatusCommand : Command
{
    public StatusCommand() : base("status", "Show build status for subsystems")
    {
        var subsystemArgument = new Argument<string?>("subsystem")
        {
            DefaultValueFactory = _ => null,
            Description = "Subsystem name to show status for (shows all if omitted)"
        };

        var verboseOption = new Option<bool>("--verbose", "-v")
        {
            Description = "Show detailed information"
        };

        Add(subsystemArgument);
        Add(verboseOption);

        this.SetAction(async parseResult =>
        {
            await ExecuteAsync(parseResult.GetValue(subsystemArgument),
                               parseResult.GetValue(verboseOption)).ConfigureAwait(false);
        });
    }

    private async Task ExecuteAsync(string? subsystemName, bool verbose)
    {
        var workingDirectory = Directory.GetCurrentDirectory();

        var context = new BuildContext
        {
            WorkingDirectory = workingDirectory,
            Verbose = verbose,
            DryRun = true
        };

        var discoverer = new SubsystemDiscoverer(context);
        var subsystems = await discoverer.DiscoverAsync().ConfigureAwait(false);

        if (subsystems.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No subsystems found[/]");
            return;
        }

        if (!string.IsNullOrEmpty(subsystemName))
        {
            subsystems = subsystems
                .Where(s => s.Name.Equals(subsystemName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (subsystems.Count == 0)
            {
                AnsiConsole.MarkupLine($"[red]✗ Subsystem '{subsystemName}' not found[/]");
                return;
            }
        }

        var stateManager = new BuildStateManager(context, discoverer);

        AnsiConsole.Write(new Rule("[blue]Build Status[/]").RuleStyle("dim"));
        AnsiConsole.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Subsystem")
            .AddColumn("Status")
            .AddColumn("Last Build")
            .AddColumn("Dependencies")
            .AddColumn("Tests");

        var totalCount = subsystems.Count;
        var builtCount = 0;
        var upToDateCount = 0;
        var staleCount = 0;
        var failedCount = 0;

        foreach (var subsystem in subsystems)
        {
            var state = stateManager.GetSubsystemState(subsystem.Name);
            var statusMarkup = GetStatusMarkup(subsystem, state, stateManager, out var statusCategory);
            var lastBuildMarkup = GetLastBuildTime(state);
            var dependenciesMarkup = GetDependencyStatus(subsystem, stateManager);
            var testsMarkup = GetTestStatus(subsystem, state, context);

            table.AddRow(
                subsystem.Name,
                statusMarkup,
                lastBuildMarkup,
                dependenciesMarkup,
                testsMarkup);

            switch (statusCategory)
            {
                case "up-to-date":
                    builtCount++;
                    upToDateCount++;
                    break;
                case "stale":
                    builtCount++;
                    staleCount++;
                    break;
                case "failed":
                    builtCount++;
                    failedCount++;
                    break;
            }

            if (verbose)
            {

                var detailRow = $"[dim]    {subsystem.Directory}[/]";
                if (subsystem.SolutionPath != null)
                {
                    detailRow += $"\n    [dim]{Path.GetFileName(subsystem.SolutionPath)}[/]";
                }
                table.AddRow(string.Empty, detailRow, string.Empty, string.Empty, string.Empty);
            }
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        var summary = new Panel(
            $"Total: [cyan]{totalCount}[/] | " +
            $"Built: [cyan]{builtCount}[/] | " +
            $"Up-to-date: [green]{upToDateCount}[/] | " +
            $"Stale: [yellow]{staleCount}[/]" +
            (failedCount > 0 ? $" | Failed: [red]{failedCount} ✗[/]" : string.Empty))
            .Border(BoxBorder.None)
            .Padding(0, 0);

        AnsiConsole.Write(summary);
    }

    private string GetStatusMarkup(
        SubsystemDefinition subsystem,
        SubsystemBuildState? state,
        BuildStateManager stateManager,
        out string category)
    {
        if (state == null)
        {
            category = "not-built";
            return "[dim]Not Built[/]";
        }

        if (!state.BuildSucceeded)
        {
            category = "failed";
            return "[red]✗ Failed[/]";
        }

        if (stateManager.IsSubsystemStale(subsystem))
        {
            category = "stale";
            return "[yellow]⚠ Stale[/]";
        }

        category = "up-to-date";
        return "[green]✓ Up-to-date[/]";
    }

    private string GetLastBuildTime(SubsystemBuildState? state)
    {
        if (state == null)
        {
            return "[dim]Never[/]";
        }

        var elapsed = DateTime.UtcNow - state.LastBuildTime;

        if (elapsed.TotalMinutes < 1)
        {
            return "[dim]Just now[/]";
        }
        if (elapsed.TotalMinutes < 60)
        {
            return $"[dim]{(int)elapsed.TotalMinutes}m ago[/]";
        }
        if (elapsed.TotalHours < 24)
        {
            return $"[dim]{(int)elapsed.TotalHours}h ago[/]";
        }
        return $"[dim]{(int)elapsed.TotalDays}d ago[/]";
    }

    private string GetDependencyStatus(SubsystemDefinition subsystem, BuildStateManager stateManager)
    {
        if (subsystem.Dependencies.Count == 0)
        {
            return "[dim]None[/]";
        }

        var okCount = 0;
        foreach (var depName in subsystem.Dependencies)
        {
            var depState = stateManager.GetSubsystemState(depName);
            if (depState != null && depState.BuildSucceeded)
            {
                okCount++;
            }
        }

        if (okCount == subsystem.Dependencies.Count)
        {
            return $"[green]{okCount} OK[/]";
        }

        return $"[yellow]{okCount}/{subsystem.Dependencies.Count} pending[/]";
    }

    private string GetTestStatus(SubsystemDefinition subsystem, SubsystemBuildState? state, BuildContext context)
    {
        if (subsystem.Test?.Projects.Count == 0)
        {
            return "[dim]None[/]";
        }

        var testCount = subsystem.Test?.Projects.Count ?? 0;
        var latestLogPath = Path.Combine(context.LogDirectory, $"test-{subsystem.Name}-latest.log");

        if (File.Exists(latestLogPath))
        {

            var logContent = File.ReadAllText(latestLogPath);
            if (logContent.Contains("Passed!") || logContent.Contains("Test Run Successful"))
            {
                return $"[green]✓ {testCount} pass[/]";
            }
            else if (logContent.Contains("Failed!") || logContent.Contains("Test Run Failed"))
            {
                return $"[red]✗ {testCount} fail[/]";
            }
        }

        return $"[dim]{testCount} proj[/]";
    }
}
