using System.CommandLine;
using Atelier.Build.Commands.Utilities;
using Atelier.Build.MetaOptimization;
using Atelier.Build.Pipeline;
using Spectre.Console;

namespace Atelier.Build.Commands;

public class BaselineCommand : BaseObservabilityCommand
{
    public BaselineCommand() : base("baseline", "Manage performance baselines")
    {

        var recordCommand = CreateRecordCommand();
        var compareCommand = CreateCompareCommand();
        var updateCommand = CreateUpdateCommand();
        var showCommand = CreateShowCommand();

        Add(recordCommand);
        Add(compareCommand);
        Add(updateCommand);
        Add(showCommand);
    }

        private Command CreateRecordCommand()
    {
        var command = new Command("record", "Record new baseline from latest benchmark results");

        var subsystemArgument = new Argument<string>("subsystem")
        {
            Description = "Subsystem name"
        };

        var reasonOption = new Option<string?>("--reason", "-r")
        {
            DefaultValueFactory = _ => null,
            Description = "Reason for baseline creation (optional)"
        };

        var userOption = new Option<string?>("--user", "-u")
        {
            DefaultValueFactory = _ => null,
            Description = "User recording baseline (defaults to environment user)"
        };

        var verboseOption = new Option<bool>("--verbose", "-v")
        {
            Description = "Show detailed output"
        };

        command.Add(subsystemArgument);
        command.Add(reasonOption);
        command.Add(userOption);
        command.Add(verboseOption);

        command.SetAction(async parseResult =>
        {
            await RecordBaselineAsync(parseResult.GetValue(subsystemArgument)!,
                                      parseResult.GetValue(reasonOption),
                                      parseResult.GetValue(userOption),
                                      parseResult.GetValue(verboseOption)).ConfigureAwait(false);
        });

        return command;
    }

        private Command CreateCompareCommand()
    {
        var command = new Command("compare", "Compare current results to baseline");

        var subsystemArgument = new Argument<string>("subsystem")
        {
            Description = "Subsystem name"
        };

        var verboseOption = new Option<bool>("--verbose", "-v")
        {
            Description = "Show detailed comparison"
        };

        var formatOption = CreateFormatOption();

        command.Add(subsystemArgument);
        command.Add(verboseOption);
        command.Add(formatOption);

        command.SetAction(async parseResult =>
        {
            await CompareToBaselineAsync(parseResult.GetValue(subsystemArgument)!,
                                         parseResult.GetValue(verboseOption),
                                         parseResult.GetValue(formatOption)!).ConfigureAwait(false);
        });

        return command;
    }

        private Command CreateUpdateCommand()
    {
        var command = new Command("update", "Update baseline (requires --approve flag)");

        var subsystemArgument = new Argument<string>("subsystem")
        {
            Description = "Subsystem name"
        };

        var approveOption = new Option<bool>("--approve")
        {
            Description = "Approve baseline update (required for safety)"
        };

        var reasonOption = new Option<string?>("--reason", "-r")
        {
            DefaultValueFactory = _ => null,
            Description = "Reason for baseline update (skips prompt if provided)"
        };

        var userOption = new Option<string?>("--user", "-u")
        {
            DefaultValueFactory = _ => null,
            Description = "User updating baseline (defaults to environment user)"
        };

        var verboseOption = new Option<bool>("--verbose", "-v")
        {
            Description = "Show detailed output"
        };

        command.Add(subsystemArgument);
        command.Add(approveOption);
        command.Add(reasonOption);
        command.Add(userOption);
        command.Add(verboseOption);

        command.SetAction(async parseResult =>
        {
            await UpdateBaselineAsync(parseResult.GetValue(subsystemArgument)!,
                                      parseResult.GetValue(approveOption),
                                      parseResult.GetValue(reasonOption),
                                      parseResult.GetValue(userOption),
                                      parseResult.GetValue(verboseOption)).ConfigureAwait(false);
        });

        return command;
    }

        private Command CreateShowCommand()
    {
        var command = new Command("show", "Display current baseline");

        var subsystemArgument = new Argument<string>("subsystem")
        {
            Description = "Subsystem name"
        };

        var historyOption = new Option<bool>("--history")
        {
            Description = "Show baseline update history"
        };

        var verboseOption = new Option<bool>("--verbose", "-v")
        {
            Description = "Show detailed baseline data"
        };

        var formatOption = CreateFormatOption();

        command.Add(subsystemArgument);
        command.Add(historyOption);
        command.Add(verboseOption);
        command.Add(formatOption);

        command.SetAction(async parseResult =>
        {
            await ShowBaselineAsync(parseResult.GetValue(subsystemArgument)!,
                                    parseResult.GetValue(historyOption),
                                    parseResult.GetValue(verboseOption),
                                    parseResult.GetValue(formatOption)!).ConfigureAwait(false);
        });

        return command;
    }


    private async Task RecordBaselineAsync(string subsystemName, string? reason, string? user, bool verbose)
    {
        var context = CreateBuildContext(verbose);
        var baselineManager = new BaselineManager(context);

        var latestCsv = FindLatestBenchmarkResults(context, subsystemName);
        if (latestCsv == null)
        {
            AnsiConsole.MarkupLine($"[red]No benchmark results found for {subsystemName}[/]");
            AnsiConsole.MarkupLine($"[dim]Run: smash {subsystemName} -b[/]");
            return;
        }

        AnsiConsole.MarkupLine($"Recording baseline for [cyan]{subsystemName}[/]...");

        try
        {

            var results = BenchmarkResultReader.FromFile(latestCsv, subsystemName);

            if (verbose)
            {
                AnsiConsole.MarkupLine($"[dim]Parsed benchmark results: {latestCsv}[/]");
                AnsiConsole.MarkupLine($"[dim]Platform: {results.Platform.Cpu}[/]");
                AnsiConsole.MarkupLine($"[dim]Timestamp: {results.Timestamp:yyyy-MM-dd HH:mm:ss}[/]");
            }

            var updatedBy = user ?? GetCurrentUser();
            var changeReason = reason ?? "Initial baseline recording";

            await baselineManager.SaveBaselineAsync(subsystemName, results, updatedBy, changeReason).ConfigureAwait(false);

            AnsiConsole.MarkupLine($"[green]✓[/] Parsed {results.Results.Count} benchmark results");

            var baselinePath = Path.Combine(context.SolutionRoot, ".baselines", subsystemName, "benchmarks.json");
            AnsiConsole.MarkupLine($"[green]✓[/] Baseline saved to [dim]{baselinePath}[/]");
            AnsiConsole.WriteLine();

            AnsiConsole.Write(new Rule("[dim]Next steps[/]").RuleStyle("dim"));
            AnsiConsole.MarkupLine("[dim]  1. Review baseline file[/]");
            AnsiConsole.MarkupLine($"[dim]  2. Commit: git add .baselines/{subsystemName}/benchmarks.json && git commit -m \"baseline: Record {subsystemName} performance\"[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to record baseline: {ex.Message}[/]");
            if (verbose)
            {
                AnsiConsole.WriteException(ex);
            }
        }
    }

    private async Task CompareToBaselineAsync(string subsystemName, bool verbose, string format)
    {
        var context = CreateBuildContext(verbose);
        var baselineManager = new BaselineManager(context);
        var effectiveFormat = ResolveFormat(format);

        var latestCsv = FindLatestBenchmarkResults(context, subsystemName);
        if (latestCsv == null)
        {
            Environment.ExitCode = 1;
            if (effectiveFormat == "plain" || effectiveFormat == "csv" || effectiveFormat == "json")
            {
                Console.Error.WriteLine($"Error: no benchmark results found for {subsystemName}");
                return;
            }
            AnsiConsole.MarkupLine($"[red]No benchmark results found for {subsystemName}[/]");
            AnsiConsole.MarkupLine($"[dim]Run: smash {subsystemName} -b[/]");
            return;
        }

        try
        {

            var currentResults = BenchmarkResultReader.FromFile(latestCsv, subsystemName);

            var comparison = await baselineManager.CompareToBaselineAsync(subsystemName, currentResults).ConfigureAwait(false);

            if (comparison.HasRegressions)
            {
                Environment.ExitCode = 1;
            }

            switch (effectiveFormat)
            {
                case "json":
                    OutputComparisonJson(comparison);
                    return;
                case "csv":
                    OutputComparisonCsv(comparison);
                    return;
                case "plain":
                    OutputComparisonPlain(comparison);
                    return;
            }

            DisplayComparison(comparison, verbose);
        }
        catch (Exception ex)
        {
            if (effectiveFormat == "plain" || effectiveFormat == "csv" || effectiveFormat == "json")
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return;
            }
            AnsiConsole.MarkupLine($"[red]Failed to compare to baseline: {ex.Message}[/]");
            if (verbose)
            {
                AnsiConsole.WriteException(ex);
            }
        }
    }

    private async Task UpdateBaselineAsync(string subsystemName, bool approve, string? reason, string? user, bool verbose)
    {
        var context = CreateBuildContext(verbose);

        if (!approve)
        {
            AnsiConsole.MarkupLine("[yellow]Baseline update requires --approve flag for safety[/]");
            AnsiConsole.MarkupLine($"[dim]Usage: smash baseline update {subsystemName} --approve[/]");
            return;
        }

        var baselineManager = new BaselineManager(context);

        var latestCsv = FindLatestBenchmarkResults(context, subsystemName);
        if (latestCsv == null)
        {
            AnsiConsole.MarkupLine($"[red]No benchmark results found for {subsystemName}[/]");
            AnsiConsole.MarkupLine($"[dim]Run: smash {subsystemName} -b[/]");
            return;
        }

        AnsiConsole.MarkupLine($"Updating baseline for [cyan]{subsystemName}[/]...");

        try
        {

            var currentResults = BenchmarkResultReader.FromFile(latestCsv, subsystemName);

            var updatedBy = user ?? GetCurrentUser();

            var changeReason = reason;
            if (string.IsNullOrWhiteSpace(changeReason))
            {
                if (Console.IsInputRedirected)
                {
                    AnsiConsole.MarkupLine("[red]A change reason is required. Pass --reason \"<text>\" when input is not interactive.[/]");
                    return;
                }
                changeReason = AnsiConsole.Prompt(
                    new TextPrompt<string>("[yellow]Enter change reason:[/]")
                        .PromptStyle("cyan")
                        .ValidationErrorMessage("[red]Reason cannot be empty[/]")
                        .Validate(r => !string.IsNullOrWhiteSpace(r)));
            }

            await baselineManager.UpdateBaselineAsync(subsystemName, currentResults, updatedBy, changeReason).ConfigureAwait(false);

            AnsiConsole.MarkupLine("[green]✓[/] Baseline updated");
            AnsiConsole.MarkupLine("[green]✓[/] History entry added");
            AnsiConsole.WriteLine();

            var baselinePath = Path.Combine(context.SolutionRoot, ".baselines", subsystemName, "benchmarks.json");
            AnsiConsole.Write(new Rule("[dim]Next steps[/]").RuleStyle("dim"));
            AnsiConsole.MarkupLine($"[dim]  1. Review changes: git diff .baselines/{subsystemName}/benchmarks.json[/]");
            AnsiConsole.MarkupLine($"[dim]  2. Commit: git add .baselines/{subsystemName}/benchmarks.json && git commit -m \"baseline: {changeReason}\"[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to update baseline: {ex.Message}[/]");
            if (verbose)
            {
                AnsiConsole.WriteException(ex);
            }
        }
    }

    private async Task ShowBaselineAsync(string subsystemName, bool showHistory, bool verbose, string format)
    {
        var context = CreateBuildContext(verbose);
        var baselineManager = new BaselineManager(context);
        var effectiveFormat = ResolveFormat(format);

        var baseline = await baselineManager.LoadBaselineAsync(subsystemName).ConfigureAwait(false);

        if (baseline == null)
        {
            if (effectiveFormat == "plain" || effectiveFormat == "csv" || effectiveFormat == "json")
            {

                return;
            }
            AnsiConsole.MarkupLine($"[yellow]No baseline found for {subsystemName}[/]");
            AnsiConsole.MarkupLine($"[dim]Run: smash baseline record {subsystemName}[/]");
            return;
        }

        switch (effectiveFormat)
        {
            case "json":
                OutputBaselineJson(baseline);
                return;
            case "csv":
                OutputBaselineCsv(baseline);
                return;
            case "plain":
                OutputBaselinePlain(baseline);
                return;
        }

        DisplayBaseline(baseline, showHistory, verbose);
    }


    private void DisplayComparison(BaselineComparison comparison, bool verbose)
    {
        if (comparison.BaselineTimestamp == DateTime.MinValue)
        {
            AnsiConsole.MarkupLine($"[yellow]⚠ No baseline found for {comparison.Subsystem}[/]");
            AnsiConsole.MarkupLine($"[dim]   Run 'smash baseline record {comparison.Subsystem}' to create baseline[/]");
            return;
        }

        AnsiConsole.Write(new Rule($"[blue]Baseline Comparison: {comparison.Subsystem}[/]").RuleStyle("dim"));
        AnsiConsole.MarkupLine($"[dim]Baseline recorded: {comparison.BaselineTimestamp:yyyy-MM-dd}[/]");
        AnsiConsole.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Category")
            .AddColumn("Count");

        table.AddRow("Regressions", $"[{(comparison.Regressions.Count > 0 ? "red" : "dim")}]{comparison.Regressions.Count}[/]");
        table.AddRow("Improvements", $"[{(comparison.Improvements.Count > 0 ? "green" : "dim")}]{comparison.Improvements.Count}[/]");
        table.AddRow("Stable", $"[dim]{comparison.Stable.Count}[/]");
        table.AddRow("New benchmarks", $"[dim]{comparison.NewBenchmarks.Count}[/]");
        table.AddRow("Removed benchmarks", $"[dim]{comparison.RemovedBenchmarks.Count}[/]");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        if (comparison.Regressions.Count > 0)
        {
            AnsiConsole.Write(new Rule("[red]Regressions[/]").RuleStyle("dim"));
            foreach (var delta in comparison.Regressions)
            {
                var sign = delta.PercentChange > 0 ? "+" : string.Empty;
                AnsiConsole.MarkupLine($"  [red]⚠[/] {delta.BenchmarkName}: {delta.BaselineMean:F2}ns → {delta.CurrentMean:F2}ns ([red]{sign}{delta.PercentChange:F1}%[/])");

                if (verbose)
                {
                    AnsiConsole.MarkupLine($"    [dim]Category: {delta.Category}, StdDev: {delta.CurrentStdDev:F2}ns[/]");
                }
            }
            AnsiConsole.WriteLine();
        }

        if (comparison.Improvements.Count > 0)
        {
            AnsiConsole.Write(new Rule("[green]Improvements[/]").RuleStyle("dim"));
            foreach (var delta in comparison.Improvements.Take(5))
            {
                var sign = delta.PercentChange > 0 ? "+" : string.Empty;
                AnsiConsole.MarkupLine($"  [green]✓[/] {delta.BenchmarkName}: {delta.BaselineMean:F2}ns → {delta.CurrentMean:F2}ns ([green]{sign}{delta.PercentChange:F1}%[/])");
            }
            if (comparison.Improvements.Count > 5)
            {
                AnsiConsole.MarkupLine($"  [dim]... and {comparison.Improvements.Count - 5} more[/]");
            }
            AnsiConsole.WriteLine();
        }

        if (comparison.Regressions.Count > 0)
        {
            AnsiConsole.Write(new Rule("[dim]Next steps[/]").RuleStyle("dim"));
            AnsiConsole.MarkupLine("[dim]  Possible causes:[/]");
            AnsiConsole.MarkupLine("[dim]    - Code change introduced overhead[/]");
            AnsiConsole.MarkupLine("[dim]    - Hardware throttling (check thermals)[/]");
            AnsiConsole.MarkupLine("[dim]    - Background processes interfering[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[dim]  To update baseline (if intentional): smash baseline update {comparison.Subsystem} --approve[/]");
        }
    }

    private void DisplayBaseline(BaselineData baseline, bool showHistory, bool verbose)
    {
        AnsiConsole.Write(new Rule($"[blue]Baseline: {baseline.Subsystem}[/]").RuleStyle("dim"));
        AnsiConsole.WriteLine();

        var grid = new Grid()
            .AddColumn()
            .AddColumn();

        grid.AddRow("Last Updated:", baseline.LastUpdated.ToString("yyyy-MM-dd HH:mm:ss"));
        grid.AddRow("Updated By:", baseline.UpdatedBy);
        grid.AddRow("Reason:", baseline.ChangeReason);
        grid.AddRow("Benchmarks:", baseline.Benchmarks.Count.ToString());
        grid.AddRow("Platform:", $"{baseline.Platform.Cpu} ({baseline.Platform.Os})");

        AnsiConsole.Write(grid);
        AnsiConsole.WriteLine();

        if (verbose)
        {
            AnsiConsole.Write(new Rule("[dim]Benchmarks[/]").RuleStyle("dim"));
            var table = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("Benchmark")
                .AddColumn("Category")
                .AddColumn("Mean")
                .AddColumn("StdDev")
                .AddColumn("Allocated");

            foreach (var bench in baseline.Benchmarks.OrderBy(b => b.Category).ThenBy(b => b.FullName))
            {
                table.AddRow(
                    bench.FullName,
                    bench.Category,
                    $"{bench.Mean:F2}ns",
                    $"{bench.StdDev:F2}ns",
                    bench.Allocated > 0 ? $"{bench.Allocated} B" : "-");
            }

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
        }

        if (showHistory && baseline.History.Count > 0)
        {
            AnsiConsole.Write(new Rule("[dim]History[/]").RuleStyle("dim"));
            var table = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("Timestamp")
                .AddColumn("User")
                .AddColumn("Reason")
                .AddColumn("Commit")
                .AddColumn("Benchmarks");

            foreach (var entry in baseline.History.TakeLast(10))
            {
                table.AddRow(
                    entry.Timestamp.ToString("yyyy-MM-dd HH:mm"),
                    entry.UpdatedBy,
                    entry.ChangeReason,
                    entry.CommitHash,
                    entry.BenchmarksChanged.Count.ToString());
            }

            AnsiConsole.Write(table);
        }
    }


    private void OutputComparisonJson(BaselineComparison comparison)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            subsystem = comparison.Subsystem,
            baselineTimestamp = comparison.BaselineTimestamp,
            currentTimestamp = comparison.CurrentTimestamp,
            summary = new
            {
                regressions = comparison.Regressions.Count,
                improvements = comparison.Improvements.Count,
                stable = comparison.Stable.Count,
                newBenchmarks = comparison.NewBenchmarks.Count,
                removedBenchmarks = comparison.RemovedBenchmarks.Count
            },
            regressions = comparison.Regressions.Select(d => new
            {
                benchmark = d.BenchmarkName,
                baselineMean = d.BaselineMean,
                currentMean = d.CurrentMean,
                percentChange = d.PercentChange
            }),
            improvements = comparison.Improvements.Select(d => new
            {
                benchmark = d.BenchmarkName,
                baselineMean = d.BaselineMean,
                currentMean = d.CurrentMean,
                percentChange = d.PercentChange
            })
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(json);
    }

    private void OutputComparisonCsv(BaselineComparison comparison)
    {
        Console.WriteLine("Benchmark,BaselineMean,CurrentMean,PercentChange,Status");
        foreach (var delta in comparison.Regressions)
        {
            Console.WriteLine($"{delta.BenchmarkName},{delta.BaselineMean:F2},{delta.CurrentMean:F2},{delta.PercentChange:F1},Regression");
        }
        foreach (var delta in comparison.Improvements)
        {
            Console.WriteLine($"{delta.BenchmarkName},{delta.BaselineMean:F2},{delta.CurrentMean:F2},{delta.PercentChange:F1},Improvement");
        }
        foreach (var delta in comparison.Stable)
        {
            Console.WriteLine($"{delta.BenchmarkName},{delta.BaselineMean:F2},{delta.CurrentMean:F2},{delta.PercentChange:F1},Stable");
        }
    }

    private void OutputComparisonPlain(BaselineComparison comparison)
    {
        if (comparison.BaselineTimestamp == DateTime.MinValue)
        {
            Console.WriteLine($"No baseline for {comparison.Subsystem}");
            return;
        }

        Console.WriteLine($"Baseline: {comparison.BaselineTimestamp:yyyy-MM-dd}");
        Console.WriteLine($"Regressions: {comparison.Regressions.Count}");
        Console.WriteLine($"Improvements: {comparison.Improvements.Count}");
        Console.WriteLine($"Stable: {comparison.Stable.Count}");
        Console.WriteLine($"New: {comparison.NewBenchmarks.Count}");
        Console.WriteLine($"Removed: {comparison.RemovedBenchmarks.Count}");
    }

    private void OutputBaselineJson(BaselineData baseline)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(baseline, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(json);
    }

    private void OutputBaselineCsv(BaselineData baseline)
    {
        Console.WriteLine("Benchmark,Category,Mean,StdDev,Allocated");
        foreach (var bench in baseline.Benchmarks)
        {
            Console.WriteLine($"{bench.FullName},{bench.Category},{bench.Mean:F2},{bench.StdDev:F2},{bench.Allocated}");
        }
    }

    private void OutputBaselinePlain(BaselineData baseline)
    {
        Console.WriteLine($"Subsystem: {baseline.Subsystem}");
        Console.WriteLine($"Last Updated: {baseline.LastUpdated:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"Updated By: {baseline.UpdatedBy}");
        Console.WriteLine($"Reason: {baseline.ChangeReason}");
        Console.WriteLine($"Benchmarks: {baseline.Benchmarks.Count}");
        Console.WriteLine($"Platform: {baseline.Platform.Cpu}");
    }


    private string? FindLatestBenchmarkResults(BuildContext context, string subsystem)
    {
        var benchmarkDir = Path.Combine(context.BenchmarkResultsDirectory, subsystem);
        if (!Directory.Exists(benchmarkDir))
        {
            return null;
        }

        var latestJsonl = Path.Combine(benchmarkDir, "latest", "results.jsonl");
        if (File.Exists(latestJsonl))
        {
            return latestJsonl;
        }

        var allJsonl = Directory.GetFiles(benchmarkDir, "results.jsonl", SearchOption.AllDirectories);
        return allJsonl.Length == 0
            ? null
            : allJsonl.OrderByDescending(File.GetLastWriteTime).First();
    }

    private string GetCurrentUser()
    {
        var gitUser = Environment.GetEnvironmentVariable("GIT_AUTHOR_NAME");
        if (!string.IsNullOrWhiteSpace(gitUser))
        {
            return gitUser;
        }

        return Environment.UserName ?? "unknown";
    }
}
