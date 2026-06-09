using System.CommandLine;
using Atelier.Build.Discovery;
using Atelier.Build.Pipeline;
using Atelier.Build.Services;
using Spectre.Console;

namespace Atelier.Build.Commands;

public class WatchCommand : Command
{
    public WatchCommand() : base("watch", "Watch a subsystem and rebuild on file changes")
    {
        var subsystemArgument = new Argument<string>("subsystem")
        {
            Description = "Subsystem name to watch (axiom, ws, field, pond, etc.)"
        };

        var verboseOption = new Option<bool>("--verbose", "-v")
        {
            Description = "Enable verbose output"
        };

        Add(subsystemArgument);
        Add(verboseOption);

        this.SetAction(async parseResult =>
        {
            await ExecuteAsync(parseResult.GetValue(subsystemArgument)!,
                               parseResult.GetValue(verboseOption)).ConfigureAwait(false);
        });
    }

    private async Task ExecuteAsync(string subsystemName, bool verbose)
    {
        var workingDirectory = Directory.GetCurrentDirectory();

        var context = new BuildContext
        {
            WorkingDirectory = workingDirectory,
            SubsystemName = subsystemName,
            Verbose = verbose,
            DryRun = false,
            IncrementalBuild = true
        };

        var discoverer = new SubsystemDiscoverer(context);
        var subsystem = await discoverer.GetByNameAsync(subsystemName).ConfigureAwait(false);

        if (subsystem == null)
        {
            AnsiConsole.MarkupLine($"[red]✗ Subsystem '{subsystemName}' not found[/]");
            var available = await discoverer.DiscoverAsync().ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[dim]Available: {string.Join(", ", available.Select(s => s.Name))}[/]");
            Environment.ExitCode = 1;
            return;
        }

        AnsiConsole.Write(new Rule($"[blue]Watch Mode: {subsystem.Name}[/]").RuleStyle("dim"));
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Starting watch mode...[/]");
        AnsiConsole.WriteLine();

        await BuildSubsystemAsync(context).ConfigureAwait(false);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[cyan]Watching {subsystem.Directory}[/]");
        AnsiConsole.MarkupLine("[dim]Press Ctrl+C to stop[/]");
        AnsiConsole.WriteLine();

        var patterns = new[] { "*.cs", "*.csproj", "smash.yml", "*.proto" };
        using var watcher = new WatchService(subsystem.Directory, patterns, debounceMs: 500);

        var cancellationSource = new CancellationTokenSource();

        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            cancellationSource.Cancel();
        };

        var rebuildBusy = 0;
        var rebuildPending = 0;

        watcher.FilesChanged += (sender, e) =>
        {
            if (cancellationSource.Token.IsCancellationRequested)
            {
                return;
            }

            Interlocked.Exchange(ref rebuildPending, 1);

            if (Interlocked.CompareExchange(ref rebuildBusy, 1, 0) != 0)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                while (Interlocked.Exchange(ref rebuildPending, 0) == 1)
                {
                    if (cancellationSource.Token.IsCancellationRequested)
                    {
                        break;
                    }

                    AnsiConsole.Clear();

                    var table = new Table()
                        .Border(TableBorder.Rounded)
                        .Title("[yellow]Files Changed[/]")
                        .AddColumn("File")
                        .AddColumn("Relative Path");

                    foreach (var file in e.ChangedFiles)
                    {
                        var relativePath = Path.GetRelativePath(subsystem.Directory, file);
                        var fileName = Path.GetFileName(file);
                        table.AddRow(fileName, relativePath);
                    }

                    AnsiConsole.Write(table);
                    AnsiConsole.WriteLine();

                    await BuildSubsystemAsync(context).ConfigureAwait(false);

                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine("[dim]Watching for changes...[/]");
                }

                Interlocked.Exchange(ref rebuildBusy, 0);
            });
        };

        watcher.Start();

        try
        {
            await Task.Delay(Timeout.Infinite, cancellationSource.Token).ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]Watch mode stopped[/]");
        }
    }

    private async Task BuildSubsystemAsync(BuildContext context)
    {
        var pipeline = new BuildPipeline(context);

        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = await pipeline.TraverseAsync().ConfigureAwait(false);
            stopwatch.Stop();

            if (result.IsSuccess)
            {
                AnsiConsole.MarkupLine($"[green]✓ Build completed in {stopwatch.Elapsed.TotalSeconds:F1}s[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]✗ Build failed: {result.Error}[/]");
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗ Build error: {ex.Message}[/]");
            if (context.Verbose)
            {
                AnsiConsole.WriteException(ex);
            }
        }
    }
}
