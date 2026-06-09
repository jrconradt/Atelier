using System.Diagnostics;
using System.Runtime.InteropServices;
using Atelier.Build.Discovery;
using Atelier.Build.Pipeline;
using Atelier.Build.Utils;
using Vice.Core;
using Vice.Contracts;
using Vice.Display.Rendering;
using static Vice.Core.Dsl;

namespace Atelier.Build.Commands;

public static class ViceCommands
{
    public static void Register(IViceApp app)
    {
        RegisterSmash(app);
        RegisterAllsmash(app);
        RegisterTest(app);
        RegisterKill(app);
    }

    private static void RegisterTest(IViceApp app)
    {
        app.Register(
            verb("test") * target("filter", required: false),
            "Run the generated test suite in-process",
            async (ctx, ct) =>
            {
                var filter = ctx.GetTarget("filter");
                var dryRun = ctx.DryRun;
                var allowlistPath = ctx.GetGlobalOption("nf-allowlist");

                var maxNf = 0;
                var maxNfRaw = ctx.GetGlobalOption("max-nf");
                if (maxNfRaw is { Length: > 0 }
                    && int.TryParse(maxNfRaw, out var parsedMaxNf))
                {
                    maxNf = parsedMaxNf;
                }

                var buildContext = new BuildContext
                {
                    WorkingDirectory = Directory.GetCurrentDirectory(),
                    Verbose = ctx.Verbose,
                    DryRun = dryRun
                };

                var harness = new GeneratedTestHarness(buildContext);

                try
                {
                    var outcome = await harness.RunAsync(new GeneratedTestOptions(dryRun,
                                                                                  filter,
                                                                                  maxNf,
                                                                                  allowlistPath)).ConfigureAwait(false);
                    return outcome.ExitCode;
                }
                catch (Exception ex)
                {
                    ctx.Console.WriteError(ex.ToString());
                    return 1;
                }
            });
    }


    private static void RegisterSmash(IViceApp app)
    {
        app.Register(
            verb("smash") * target("target", required: false),
            "Build boutiques, subsystems, or generate artifacts",
            async (ctx, ct) =>
            {
                var path = ctx.GetTarget("target");
                var verbose = ctx.Verbose;
                var dryRun = ctx.DryRun;
                var diagram = ctx.HasGlobalOption("diagram");
                var generateBoutiques = ctx.HasGlobalOption("generate-boutiques");
                var runTests = ctx.HasGlobalOption("test");
                var runBenchmarks = ctx.HasGlobalOption("benchmark");
                var allowBenchmarkRegression = ctx.HasGlobalOption("allow-benchmark-regression");
                var noIncremental = ctx.HasGlobalOption("no-incremental");
                var noCoverage = ctx.HasGlobalOption("no-coverage");

                var workingDirectory = Directory.GetCurrentDirectory();
                var subsystemName = await DetectSubsystemAsync(workingDirectory, path).ConfigureAwait(false);

                var buildContext = new BuildContext
                {
                    WorkingDirectory = workingDirectory,
                    ProjectPath = subsystemName == null ? path : null,
                    SubsystemName = subsystemName,
                    Verbose = verbose,
                    DryRun = dryRun,
                    GenerateDiagram = diagram,
                    GenerateBoutiques = generateBoutiques,
                    RunTests = runTests,
                    RunBenchmarks = runBenchmarks,
                    AllowBenchmarkRegression = allowBenchmarkRegression,
                    IncrementalBuild = !noIncremental,
                    DisableCoverage = noCoverage
                };

                var pipeline = new BuildPipeline(buildContext);

                try
                {
                    var result = await pipeline.TraverseAsync().ConfigureAwait(false);

                    if (result.IsSuccess)
                    {
                        ctx.Console.WriteLine("✓ Build completed successfully");
                        return 0;
                    }
                    else
                    {
                        ctx.Console.WriteError($"✗ Build failed: {result.Error}");
                        return 1;
                    }
                }
                catch (Exception ex)
                {
                    ctx.Console.WriteError(ex.ToString());
                    return 1;
                }
            });
    }

        private static async Task<string?> DetectSubsystemAsync(string workingDirectory, string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        if (path.Contains('/') || path.Contains('\\') ||
            path.EndsWith(".sln") || path.EndsWith(".csproj"))
        {
            return null;
        }

        try
        {
            var context = new BuildContext
            {
                WorkingDirectory = workingDirectory,
                Verbose = false,
                DryRun = true
            };

            var discoverer = new SubsystemDiscoverer(context);
            var subsystems = await discoverer.DiscoverAsync().ConfigureAwait(false);

            var match = subsystems.FirstOrDefault(s =>
                s.Name.Equals(path, StringComparison.OrdinalIgnoreCase));

            return match?.Name;
        }
        catch
        {

            return null;
        }
    }


    private static void RegisterAllsmash(IViceApp app)
    {
        app.Register(
            verb("allsmash"),
            "Full pipeline: clean → generate → build → docker rebuild",
            async (ctx, ct) =>
            {
                var verbose = ctx.Verbose;
                var skipDocker = ctx.HasGlobalOption("skip-docker");
                var workingDirectory = Directory.GetCurrentDirectory();

                ctx.Render.WriteRule("allsmash - Full Pipeline");
                ctx.Console.WriteLine();

                var steps = new List<(string Name, Func<Task<bool>> Action)>
                {
                    ("Clean all artifacts", () => RunAllsmashCleanAsync(workingDirectory)),
                    ("Generate boutique projects", () => RunAllsmashGenerateAsync(workingDirectory, verbose)),
                    ("Build boutiques", () => RunAllsmashBuildAsync(workingDirectory, verbose))
                };

                if (!skipDocker)
                {
                    steps.Add(("Stop Docker containers", () => RunAllsmashDockerDownAsync(workingDirectory, verbose, ctx.Console)));
                    steps.Add(("Build Docker images", () => RunAllsmashDockerBuildAsync(workingDirectory, verbose, ctx.Console)));
                    steps.Add(("Start Docker containers", () => RunAllsmashDockerUpAsync(workingDirectory, verbose, ctx.Console)));
                }

                var stepNumber = 1;
                var totalSteps = steps.Count;

                foreach (var (name, action) in steps)
                {
                    ctx.Console.WriteLine($"[{stepNumber}/{totalSteps}] {name}...");

                    try
                    {
                        var success = await action().ConfigureAwait(false);
                        if (!success)
                        {
                            ctx.Console.WriteError($"✗ Failed: {name}");
                            return 1;
                        }
                        ctx.Console.WriteLine($"  ✓ {name} completed");
                    }
                    catch (Exception ex)
                    {
                        ctx.Console.WriteError($"✗ {name} failed with exception");
                        if (verbose)
                        {
                            ctx.Console.WriteError(ex.ToString());
                        }
                        return 1;
                    }

                    stepNumber++;
                    ctx.Console.WriteLine();
                }

                ctx.Render.WriteRule("Pipeline Complete");
                return 0;
            });
    }

    private static async Task<bool> RunAllsmashCleanAsync(string workingDirectory)
    {
        var context = new BuildContext
        {
            WorkingDirectory = workingDirectory
        };

        var cleaner = new ArtifactCleaner(context);
        await cleaner.CleanAsync(all: true, docker: false).ConfigureAwait(false);
        return true;
    }

    private static async Task<bool> RunAllsmashGenerateAsync(string workingDirectory, bool verbose)
    {
        var context = new BuildContext
        {
            WorkingDirectory = workingDirectory,
            Verbose = verbose,
            GenerateBoutiques = true
        };

        var pipeline = new BuildPipeline(context);
        var result = await pipeline.TraverseAsync().ConfigureAwait(false);
        return result.IsSuccess;
    }

    private static async Task<bool> RunAllsmashBuildAsync(string workingDirectory, bool verbose)
    {
        var context = new BuildContext
        {
            WorkingDirectory = workingDirectory,
            Verbose = verbose,
            GenerateBoutiques = false
        };

        var pipeline = new BuildPipeline(context);
        var result = await pipeline.TraverseAsync().ConfigureAwait(false);
        return result.IsSuccess;
    }

    private static async Task<bool> RunAllsmashDockerDownAsync(string workingDirectory, bool verbose, IConsoleWriter console)
    {
        return await RunAllsmashProcessAsync("docker-compose", "down -v", workingDirectory, verbose, console).ConfigureAwait(false);
    }

    private static async Task<bool> RunAllsmashDockerBuildAsync(string workingDirectory, bool verbose, IConsoleWriter console)
    {
        return await RunAllsmashProcessAsync("docker-compose", "build", workingDirectory, verbose, console).ConfigureAwait(false);
    }

    private static async Task<bool> RunAllsmashDockerUpAsync(string workingDirectory, bool verbose, IConsoleWriter console)
    {
        return await RunAllsmashProcessAsync("docker-compose", "up -d", workingDirectory, verbose, console).ConfigureAwait(false);
    }

    private static async Task<bool> RunAllsmashProcessAsync(
        string command,
        string arguments,
        string workingDirectory,
        bool verbose,
        IConsoleWriter console)
    {
        var executor = new ProcessExecutor(new BuildContext
        {
            WorkingDirectory = workingDirectory,
            Verbose = verbose
        });

        var options = verbose
            ? ProcessOptions.WithTimeoutAndCallbacks(
                TimeSpan.FromMinutes(30),
                onOutputLine: line => console.WriteLine($"  {line}"),
                onErrorLine: line => console.WriteError($"  {line}"))
            : ProcessOptions.WithTimeout(TimeSpan.FromMinutes(30));

        try
        {
            var result = await executor.ExecuteAsync(
                command, arguments, workingDirectory, options).ConfigureAwait(false);

            if (!result.Success && !verbose)
            {
                var error = result.StandardError;
                if (!string.IsNullOrWhiteSpace(error))
                {
                    console.WriteError(error);
                }
            }

            return result.Success;
        }
        catch (ProcessExecutionException)
        {
            console.WriteError($"  Command '{command}' not found. Is Docker installed?");
            return false;
        }
    }



    private static void RegisterKill(IViceApp app)
    {
        app.Register(
            verb("kill"),
            "Kill orphaned dotnet host processes",
            ExecuteKillAsync);
    }

    private static async Task<int> ExecuteKillAsync(CommandContext ctx, CancellationToken ct)
    {
        var pattern = ctx.GetGlobalOption("pattern");
        var force = ctx.HasGlobalOption("force");
        var dryRun = ctx.DryRun;
        var verbose = ctx.Verbose;

        ctx.Render.WriteRule("smash kill");
        ctx.Console.WriteLine();

        var currentProcessId = Environment.ProcessId;
        var excludedProcessIds = KillGetProcessChain(currentProcessId);

        var dotnetProcesses = KillEnumerateDotnetProcesses(excludedProcessIds);

        if (dotnetProcesses.Count == 0)
        {
            ctx.Console.WriteLine("No dotnet processes found (excluding current process chain)");
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(pattern))
        {
            dotnetProcesses = KillFilterByPattern(dotnetProcesses, pattern);

            if (dotnetProcesses.Count == 0)
            {
                ctx.Console.WriteLine($"No dotnet processes found matching pattern '{pattern}'");
                return 0;
            }
        }

        KillDisplayProcessTable(dotnetProcesses, verbose, ctx.Render);
        ctx.Console.WriteLine();

        if (!force && !dryRun)
        {
            var confirm = KillConfirm(ctx.Console, $"Kill {dotnetProcesses.Count} dotnet process(es)?");
            if (!confirm)
            {
                ctx.Console.WriteLine("Cancelled");
                return 0;
            }
        }

        if (dryRun)
        {
            ctx.Console.WriteLine($"Dry run mode - would kill {dotnetProcesses.Count} process(es)");
            return 0;
        }

        return await KillProcessesAsync(dotnetProcesses, ctx).ConfigureAwait(false);
    }

    private static List<Process> KillEnumerateDotnetProcesses(HashSet<int> excludedProcessIds)
    {
        return Process.GetProcesses()
            .Where(KillIsDotnetProcess)
            .Where(p => !excludedProcessIds.Contains(p.Id))
            .ToList();
    }

    private static bool KillIsDotnetProcess(Process process)
    {
        try
        {
            return process.ProcessName.Equals("dotnet", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static List<Process> KillFilterByPattern(List<Process> processes, string pattern)
    {
        return processes.Where(p => KillMatchesPattern(p, pattern)).ToList();
    }

    private static bool KillMatchesPattern(Process process, string pattern)
    {
        try
        {
            var cmdLine = KillGetProcessCommandLine(process);
            return cmdLine?.Contains(pattern, StringComparison.OrdinalIgnoreCase) ?? false;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<int> KillProcessesAsync(List<Process> processes, CommandContext ctx)
    {
        var killed = 0;
        var failed = 0;

        ctx.Console.WriteLine("Killing processes...");

        foreach (var process in processes)
        {
            try
            {
                var success = await KillProcessAsync(process).ConfigureAwait(false);
                if (success)
                {
                    killed++;
                    ctx.Console.WriteLine($"  ✓ Killed PID {process.Id}");
                }
                else
                {
                    failed++;
                    ctx.Console.WriteError($"  ✗ Failed to kill PID {process.Id}");
                }
            }
            catch (Exception ex)
            {
                failed++;
                ctx.Console.WriteError($"  ✗ Error killing PID {process.Id}: {ex.Message}");
            }
        }

        ctx.Console.WriteLine();

        var summaryTable = new Table()
            .AddColumn("Result")
            .AddColumn("Count", col => col.Alignment = Alignment.Right);

        summaryTable.AddRow("Killed", killed.ToString());
        summaryTable.AddRow("Failed", failed.ToString());
        summaryTable.AddRow("Total", processes.Count.ToString());

        ctx.Render.WriteTable(summaryTable);

        return failed > 0 ? 1 : 0;
    }

    private static bool KillConfirm(IConsoleWriter console, string message)
    {
        console.Write($"{message} [y/N] ");
        var r = Console.ReadLine()?.Trim().ToLowerInvariant();
        return r is "y" or "yes";
    }

    private static void KillDisplayProcessTable(List<Process> processes, bool verbose, RenderContext render)
    {
        var table = new Table()
            .AddColumn("PID")
            .AddColumn("Name")
            .AddColumn("Runtime")
            .AddColumn("Memory (MB)");

        if (verbose)
        {
            table.AddColumn("Command Line");
        }

        foreach (var process in processes)
        {
            try
            {
                var pid = process.Id.ToString();
                var name = process.ProcessName;
                var runtime = KillFormatRuntime(DateTime.Now - process.StartTime);
                var memoryMb = (process.WorkingSet64 / 1024.0 / 1024.0).ToString("F1");

                if (verbose)
                {
                    var cmdLine = KillGetProcessCommandLine(process) ?? "<unknown>";

                    if (cmdLine.Length > 80)
                    {
                        cmdLine = cmdLine.Substring(0, 77) + "...";
                    }
                    table.AddRow(pid, name, runtime, memoryMb, cmdLine);
                }
                else
                {
                    table.AddRow(pid, name, runtime, memoryMb);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
        }

        render.WriteTable(table);
    }

    private static string KillFormatRuntime(TimeSpan runtime)
    {
        if (runtime.TotalDays >= 1)
        {
            return $"{(int)runtime.TotalDays}d {runtime.Hours}h";
        }
        if (runtime.TotalHours >= 1)
        {
            return $"{(int)runtime.TotalHours}h {runtime.Minutes}m";
        }
        if (runtime.TotalMinutes >= 1)
        {
            return $"{(int)runtime.TotalMinutes}m {runtime.Seconds}s";
        }
        return $"{(int)runtime.TotalSeconds}s";
    }

    private static HashSet<int> KillGetProcessChain(int processId)
    {
        var chain = new HashSet<int>();
        var current = processId;

        while (current > 0)
        {
            chain.Add(current);
            try
            {
                var process = Process.GetProcessById(current);

                var parentId = KillGetParentProcessId(process);
                if (parentId > 0 && parentId != current)
                {
                    current = parentId;
                }
                else
                {
                    break;
                }
            }
            catch
            {
                break;
            }
        }

        return chain;
    }

    private static int KillGetParentProcessId(Process process)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {

                return 0;
            }
            else
            {

                var statPath = $"/proc/{process.Id}/stat";
                if (File.Exists(statPath))
                {
                    var stat = File.ReadAllText(statPath);

                    var fields = stat.Split(' ');
                    if (fields.Length > 3 && int.TryParse(fields[3], out var ppid))
                    {
                        return ppid;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
        }

        return 0;
    }

    private static string? KillGetProcessCommandLine(Process process)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {

                return null;
            }
            else
            {

                var cmdlinePath = $"/proc/{process.Id}/cmdline";
                if (File.Exists(cmdlinePath))
                {
                    var cmdline = File.ReadAllText(cmdlinePath);

                    return cmdline.Replace('\0', ' ').Trim();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
        }

        return null;
    }

    private static async Task<bool> KillProcessAsync(Process process)
    {
        try
        {
            var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            var fileName = isWindows ? "taskkill" : "kill";
            var arguments = isWindows ? $"/F /PID {process.Id}" : $"-9 {process.Id}";

            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var killProcess = new Process { StartInfo = startInfo };
            killProcess.Start();
            await killProcess.WaitForExitAsync().ConfigureAwait(false);
            return killProcess.ExitCode == 0;
        }
        catch (Exception)
        {

            try
            {
                process.Kill(entireProcessTree: true);
                await Task.Delay(100).ConfigureAwait(false);
                return process.HasExited;
            }
            catch
            {
                return false;
            }
        }
    }
}
