using System.CommandLine;
using System.Diagnostics;
using Atelier.Build.Pipeline;
using Spectre.Console;

namespace Atelier.Build.Commands;

public class UnsmashCommand : Command
{
    private static readonly TimeSpan DockerCommandTimeout = TimeSpan.FromMinutes(5);

    public UnsmashCommand() : base("unsmash", "Clean build artifacts and generated files")
    {
        var targetArgument = new Argument<string?>("target")
        {
            DefaultValueFactory = _ => null,
            Description = "Target to clean: 'docker' for Docker-only cleanup"
        };

        var allOption = new Option<bool>("--all", "-a")
        {
            Description = "Remove all artifacts including retained generations"
        };

        var dockerOption = new Option<bool>("--docker")
        {
            Description = "Also remove Docker images"
        };

        var volumesOption = new Option<bool>("--volumes", "-v")
        {
            Description = "Also remove Docker volumes (use with docker target)"
        };

        Add(targetArgument);
        Add(allOption);
        Add(dockerOption);
        Add(volumesOption);

        this.SetAction(async parseResult =>
        {
            await TraverseAsync(parseResult.GetValue(targetArgument),
                                parseResult.GetValue(allOption),
                                parseResult.GetValue(dockerOption),
                                parseResult.GetValue(volumesOption)).ConfigureAwait(false);
        });
    }

    private async Task TraverseAsync(string? target, bool all, bool docker, bool volumes)
    {
        var workingDirectory = Directory.GetCurrentDirectory();

        if (string.Equals(target, "docker", StringComparison.OrdinalIgnoreCase))
        {
            await CleanDockerAsync(workingDirectory, volumes).ConfigureAwait(false);
            return;
        }

        var context = new BuildContext
        {
            WorkingDirectory = workingDirectory
        };

        var cleaner = new ArtifactCleaner(context);

        try
        {
            await cleaner.CleanAsync(all, docker).ConfigureAwait(false);
            AnsiConsole.MarkupLine("[green]✓ Cleanup completed[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex);
            Environment.ExitCode = 1;
        }
    }

    private async Task CleanDockerAsync(string workingDirectory, bool volumes)
    {
        AnsiConsole.Write(new Rule("[bold cyan]smash unsmash docker[/]").RuleStyle("grey"));
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[yellow]Stopping Docker containers...[/]");
        var downArgs = volumes ? "down -v" : "down";
        var downSuccess = await RunDockerComposeAsync(downArgs, workingDirectory).ConfigureAwait(false);
        if (downSuccess)
        {
            AnsiConsole.MarkupLine($"[green]  ✓ Containers stopped{(volumes ? " and volumes removed" : "")}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]  Warning: docker-compose down had issues[/]");
        }
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[yellow]Removing Docker images...[/]");
        var imagesRemoved = await RemoveDockerImagesAsync().ConfigureAwait(false);
        if (imagesRemoved > 0)
        {
            AnsiConsole.MarkupLine($"[green]  ✓ Removed {imagesRemoved} image(s)[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[dim]  No Atelier images to remove[/]");
        }
        AnsiConsole.WriteLine();

        AnsiConsole.Write(new Rule("[bold green]Docker cleanup complete[/]").RuleStyle("green"));
    }

    private async Task<bool> RunDockerComposeAsync(string arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker-compose",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return false;
            }

            using var timeoutCts = new CancellationTokenSource(DockerCommandTimeout);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                KillProcessTree(process);
                AnsiConsole.MarkupLine($"[red]  docker-compose timed out after {DockerCommandTimeout}[/]");
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception)
        {
            AnsiConsole.MarkupLine("[red]  docker-compose not found. Is Docker installed?[/]");
            return false;
        }
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
        }
    }

    private async Task<int> RemoveDockerImagesAsync()
    {
        var listInfo = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = "images --filter=reference=atelier-* --filter=reference=*atelier* -q",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var listProcess = Process.Start(listInfo);
            if (listProcess == null)
            {
                return 0;
            }

            using var listTimeoutCts = new CancellationTokenSource(DockerCommandTimeout);
            string output;

            try
            {
                output = await listProcess.StandardOutput.ReadToEndAsync(listTimeoutCts.Token).ConfigureAwait(false);
                await listProcess.WaitForExitAsync(listTimeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                KillProcessTree(listProcess);
                AnsiConsole.MarkupLine($"[red]  docker images timed out after {DockerCommandTimeout}[/]");
                return 0;
            }

            var imageIds = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(id => id.Trim())
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .ToList();

            if (imageIds.Count == 0)
            {
                return 0;
            }

            var removed = 0;
            foreach (var imageId in imageIds)
            {
                var removeInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = $"rmi -f {imageId}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var removeProcess = Process.Start(removeInfo);
                if (removeProcess != null)
                {
                    using var removeTimeoutCts = new CancellationTokenSource(DockerCommandTimeout);

                    try
                    {
                        await removeProcess.WaitForExitAsync(removeTimeoutCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        KillProcessTree(removeProcess);
                        AnsiConsole.MarkupLine($"[red]  docker rmi timed out after {DockerCommandTimeout}[/]");
                        continue;
                    }

                    if (removeProcess.ExitCode == 0)
                    {
                        removed++;
                    }
                }
            }

            return removed;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception)
        {
            AnsiConsole.MarkupLine("[red]  Docker not found. Is Docker installed?[/]");
            return 0;
        }
    }
}
