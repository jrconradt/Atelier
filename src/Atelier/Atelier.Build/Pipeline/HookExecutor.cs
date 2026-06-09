using Atelier.Build.Discovery;
using Atelier.Build.Utils;
using Spectre.Console;

namespace Atelier.Build.Pipeline;

public sealed class HookExecutor
{
    private readonly BuildContext _context;
    private readonly PlatformProbe _platform;

    public HookExecutor(BuildContext context, PlatformProbe platform)
    {
        _context = context;
        _platform = platform;
    }

    public async Task<bool> ExecutePostBuildHooksAsync(SubsystemDefinition subsystem)
    {
        if (subsystem.PostBuild == null)
        {
            return true;
        }

        AnsiConsole.MarkupLine("[yellow]Post-Build Hooks:[/]");

        var platform = _platform.GetCurrentPlatform();
        var steps = platform switch
        {
            "linux" => subsystem.PostBuild.Linux,
            "windows" => subsystem.PostBuild.Windows,
            "macos" => subsystem.PostBuild.MacOS,
            _ => null
        };

        if (steps == null || steps.Count == 0)
        {
            AnsiConsole.MarkupLine($"[dim]  No post-build hooks for {platform}[/]");
            return true;
        }

        foreach (var step in steps)
        {
            AnsiConsole.MarkupLine($"[cyan]  →[/] {step.Name}");

            var workingDir = subsystem.Directory;
            if (!string.IsNullOrWhiteSpace(step.WorkingDirectory))
            {
                workingDir = Path.Combine(subsystem.Directory, step.WorkingDirectory);
            }

            var success = await ExecuteCommandAsync(step.Command, workingDir, step.Description).ConfigureAwait(false);
            if (!success && !step.SkipIfMissing)
            {
                AnsiConsole.MarkupLine($"[red]    ✗ Post-build hook failed[/]");
                return false;
            }

            AnsiConsole.MarkupLine($"[green]    ✓[/]");
        }

        return true;
    }

    public async Task<bool> ExecutePostTestHooksAsync(
        SubsystemDefinition subsystem,
        List<string> testLogs)
    {
        if (subsystem.PostTest == null)
        {
            return true;
        }

        AnsiConsole.MarkupLine("[yellow]Post-Test Hooks:[/]");

        var platform = _platform.GetCurrentPlatform();
        var steps = platform switch
        {
            "linux" => subsystem.PostTest.Linux,
            "windows" => subsystem.PostTest.Windows,
            "macos" => subsystem.PostTest.MacOS,
            _ => null
        };

        if (steps == null || steps.Count == 0)
        {
            AnsiConsole.MarkupLine($"[dim]  No post-test hooks for {platform}[/]");
            return true;
        }

        var latestLogPath = Path.Combine(_context.LogDirectory, $"test-{subsystem.Name}-latest.log");
        Environment.SetEnvironmentVariable("SMASH_TEST_LOG", latestLogPath);
        Environment.SetEnvironmentVariable("SMASH_SUBSYSTEM", subsystem.Name);

        foreach (var step in steps)
        {
            AnsiConsole.MarkupLine($"[cyan]  →[/] {step.Name}");

            var workingDir = subsystem.Directory;
            if (!string.IsNullOrWhiteSpace(step.WorkingDirectory))
            {
                workingDir = Path.Combine(subsystem.Directory, step.WorkingDirectory);
            }

            var success = await ExecuteCommandAsync(step.Command, workingDir, step.Description).ConfigureAwait(false);
            if (!success && !step.SkipIfMissing)
            {
                AnsiConsole.MarkupLine($"[red]    ✗ Post-test hook failed[/]");
                return false;
            }

            AnsiConsole.MarkupLine($"[green]    ✓[/]");
        }

        return true;
    }

    public async Task<bool> ExecutePreBuildStepsAsync(SubsystemDefinition subsystem)
    {
        if (subsystem.PreBuild == null)
        {
            return true;
        }

        var platform = _platform.GetCurrentPlatform();
        var steps = platform switch
        {
            "linux" => subsystem.PreBuild.Linux,
            "windows" => subsystem.PreBuild.Windows,
            "macos" => subsystem.PreBuild.MacOS,
            _ => null
        };

        if (steps == null || steps.Count == 0)
        {
            AnsiConsole.MarkupLine($"[dim]  No pre-build steps for {platform}[/]");
            return true;
        }

        foreach (var step in steps)
        {
            AnsiConsole.MarkupLine($"[cyan]  →[/] {step.Name}");

            if (step.RequiredTools != null && step.RequiredTools.Count > 0)
            {
                foreach (var tool in step.RequiredTools)
                {
                    if (!_platform.IsToolAvailable(tool))
                    {
                        if (step.SkipIfMissing)
                        {
                            AnsiConsole.MarkupLine($"[yellow]    ⚠ Skipped ({tool} not found)[/]");
                            continue;
                        }
                        else
                        {
                            AnsiConsole.MarkupLine($"[red]    ✗ Required tool not found: {tool}[/]");
                            return false;
                        }
                    }
                }
            }

            var workingDir = subsystem.Directory;
            if (!string.IsNullOrWhiteSpace(step.WorkingDirectory))
            {
                workingDir = Path.Combine(subsystem.Directory, step.WorkingDirectory);
                if (!Directory.Exists(workingDir))
                {
                    AnsiConsole.MarkupLine($"[red]    ✗ Working directory not found: {workingDir}[/]");
                    return false;
                }
            }

            var success = await ExecuteCommandAsync(step.Command, workingDir, step.Description).ConfigureAwait(false);
            if (!success)
            {
                if (step.SkipIfMissing)
                {
                    AnsiConsole.MarkupLine($"[yellow]    ⚠ Skipped (command failed)[/]");
                    continue;
                }
                return false;
            }

            AnsiConsole.MarkupLine($"[green]    ✓[/]");
        }

        return true;
    }

    private async Task<bool> ExecuteCommandAsync(string command, string workingDirectory, string? description)
    {
        var tokens = ShellTokenizer.Tokenize(command);
        if (tokens.Count == 0)
        {
            AnsiConsole.MarkupLine($"[red]    ✗ Empty hook command[/]");
            return false;
        }

        var fileName = tokens[0];
        var argumentList = tokens.Skip(1).ToList();

        var executor = new ProcessExecutor(_context);
        try
        {
            var options = _context.Verbose
                ? ProcessOptions.WithTimeoutAndCallbacks(
                    _context.Timeouts.DotnetBuild,
                    onOutputLine: line => AnsiConsole.MarkupLine($"[dim]{line}[/]"))
                : ProcessOptions.WithTimeout(_context.Timeouts.DotnetBuild);

            var result = await executor.ExecuteAsync(
                fileName,
                argumentList,
                workingDirectory,
                options,
                CancellationToken.None).ConfigureAwait(false);

            if (!result.Success && !string.IsNullOrWhiteSpace(result.StandardError))
            {
                AnsiConsole.MarkupLine($"[red]{result.StandardError}[/]");
            }

            return result.Success;
        }
        catch (ProcessExecutionException ex)
        {
            AnsiConsole.MarkupLine($"[red]{ex.Message}[/]");
            return false;
        }
    }
}
