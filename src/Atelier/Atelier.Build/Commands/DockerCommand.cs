using System.CommandLine;
using System.Diagnostics;
using Atelier.Build.Pipeline;
using Atelier.Build.Utils;
using Spectre.Console;

namespace Atelier.Build.Commands;

public class DockerCommand : Command
{
    public DockerCommand() : base("docker", "Build and run Docker containers")
    {
        var verboseOption = new Option<bool>("--verbose", "-v")
        {
            Description = "Enable verbose output"
        };

        var buildOnlyOption = new Option<bool>("--build-only", "-b")
        {
            Description = "Only build images, don't start containers"
        };

        var upOnlyOption = new Option<bool>("--up-only", "-u")
        {
            Description = "Only start containers (assumes images exist)"
        };

        var detachOption = new Option<bool>("--detach", "-d")
        {
            DefaultValueFactory = _ => true,
            Description = "Run containers in background (default: true)"
        };

        var downOption = new Option<bool>("--down")
        {
            Description = "Stop and remove containers before rebuilding"
        };

        var cleanVolumesOption = new Option<bool>("--clean-volumes", "-V")
        {
            Description = "Remove volumes when stopping (use with --down)"
        };

        Add(verboseOption);
        Add(buildOnlyOption);
        Add(upOnlyOption);
        Add(detachOption);
        Add(downOption);
        Add(cleanVolumesOption);

        this.SetAction(async parseResult =>
        {
            await TraverseAsync(parseResult.GetValue(verboseOption),
                                parseResult.GetValue(buildOnlyOption),
                                parseResult.GetValue(upOnlyOption),
                                parseResult.GetValue(detachOption),
                                parseResult.GetValue(downOption),
                                parseResult.GetValue(cleanVolumesOption)).ConfigureAwait(false);
        });
    }

    private async Task TraverseAsync(
        bool verbose,
        bool buildOnly,
        bool upOnly,
        bool detach,
        bool down,
        bool cleanVolumes)
    {
        var workingDirectory = Directory.GetCurrentDirectory();

        var context = new BuildContext
        {
            WorkingDirectory = workingDirectory,
            Verbose = verbose
        };

        AnsiConsole.Write(new Rule("[bold cyan]smash docker[/]").RuleStyle("grey"));
        AnsiConsole.WriteLine();

        if (down)
        {
            AnsiConsole.MarkupLine("[yellow]Stopping containers...[/]");
            var downArgs = cleanVolumes ? "down -v" : "down";
            var downSuccess = await RunDockerComposeAsync(context, downArgs).ConfigureAwait(false);
            if (!downSuccess)
            {
                AnsiConsole.MarkupLine("[yellow]  Warning: docker-compose down had issues (continuing)[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[green]  ✓ Containers stopped[/]");
            }
            AnsiConsole.WriteLine();
        }

        if (!upOnly)
        {
            AnsiConsole.MarkupLine("[yellow]Building Docker images...[/]");
            var buildSuccess = await RunDockerComposeAsync(context, "build").ConfigureAwait(false);
            if (!buildSuccess)
            {
                AnsiConsole.MarkupLine("[red]✗ Docker build failed[/]");
                Environment.ExitCode = 1;
                return;
            }
            AnsiConsole.MarkupLine("[green]  ✓ Images built successfully[/]");
            AnsiConsole.WriteLine();
        }

        if (!buildOnly)
        {
            var upArgs = detach ? "up -d" : "up";
            AnsiConsole.MarkupLine($"[yellow]Starting containers{(detach ? " (detached)" : "")}...[/]");
            var upSuccess = await RunDockerComposeAsync(context, upArgs).ConfigureAwait(false);
            if (!upSuccess)
            {
                AnsiConsole.MarkupLine("[red]✗ Failed to start containers[/]");
                Environment.ExitCode = 1;
                return;
            }
            AnsiConsole.MarkupLine("[green]  ✓ Containers started[/]");
            AnsiConsole.WriteLine();

            if (detach)
            {
                AnsiConsole.MarkupLine("[dim]Run 'docker-compose logs -f' to view logs[/]");
                AnsiConsole.MarkupLine("[dim]Run 'docker-compose ps' to view status[/]");
            }
        }

        AnsiConsole.Write(new Rule("[bold green]Done[/]").RuleStyle("green"));
    }

    private async Task<bool> RunDockerComposeAsync(BuildContext context, string arguments)
    {
        var executor = new ProcessExecutor(context);

        var onOutputLine = context.Verbose
            ? (Action<string>?)((line) => AnsiConsole.MarkupLine($"[grey]  {Markup.Escape(line)}[/]"))
            : null;



        var onErrorLine = context.Verbose
            ? (Action<string>?)((line) => AnsiConsole.MarkupLine($"[yellow]  {Markup.Escape(line)}[/]"))
            : null;

        try
        {
            var options = ProcessOptions.WithTimeoutAndCallbacks(
                context.Timeouts.DockerBuild,
                onOutputLine,
                onErrorLine);

            var result = await executor.ExecuteAsync(
                "docker-compose",
                arguments,
                context.WorkingDirectory,
                options,
                CancellationToken.None).ConfigureAwait(false);

            if (!result.Success && !context.Verbose)
            {
                var error = result.StandardError;
                if (!string.IsNullOrWhiteSpace(error))
                {
                    AnsiConsole.MarkupLine($"[red]  {Markup.Escape(error.Trim())}[/]");
                }
            }

            return result.Success;
        }
        catch (ProcessExecutionException)
        {
            AnsiConsole.MarkupLine("[red]  docker-compose not found. Is Docker installed?[/]");
            return false;
        }
    }
}
