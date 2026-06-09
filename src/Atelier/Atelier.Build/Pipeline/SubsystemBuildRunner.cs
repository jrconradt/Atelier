using System.Diagnostics;
using Atelier.Build.Discovery;
using Atelier.Build.MetaOptimization;
using Atelier.Build.Utils;
using Spectre.Console;

namespace Atelier.Build.Pipeline;

public sealed class SubsystemBuildRunner
{
    private readonly BuildContext _context;
    private readonly BuildPresenter _presenter;
    private readonly ShellRunner _shell;
    private readonly PlatformProbe _platform;
    private readonly HookExecutor _hooks;

    public SubsystemBuildRunner(
        BuildContext context,
        BuildPresenter presenter,
        ShellRunner shell,
        PlatformProbe platform,
        HookExecutor hooks)
    {
        _context = context;
        _presenter = presenter;
        _shell = shell;
        _platform = platform;
        _hooks = hooks;
    }

    public async Task<BuildResult> ExecuteAsync(List<string> artifacts)
    {
        var stopwatch = Stopwatch.StartNew();
        var subsystemName = _context.SubsystemName!;
        var discoverer = new SubsystemDiscoverer(_context);
        var stateManager = new BuildStateManager(_context, discoverer);

        var phase = new PhaseCounter();

        var subsystem = await DiscoverSubsystemAsync(subsystemName, discoverer, phase).ConfigureAwait(false);
        if (subsystem == null)
        {
            return BuildResult.Failure($"Subsystem '{subsystemName}' not found. Available: {string.Join(", ", (await discoverer.DiscoverAsync().ConfigureAwait(false)).Select(s => s.Name))}");
        }

        if (_context.DryRun)
        {
            PreviewSubsystemDryRun(subsystem);
            return BuildResult.Success(artifacts, []);
        }

        var depResult = await BuildSubsystemDependenciesAsync(subsystem, discoverer, stateManager, phase).ConfigureAwait(false);
        if (depResult != null)
        {
            return depResult;
        }

        var preBuildResult = await RunSubsystemPreBuildAsync(subsystem, phase).ConfigureAwait(false);
        if (preBuildResult != null)
        {
            return preBuildResult;
        }

        var buildResult = await BuildSubsystemSolutionAsync(subsystem, stateManager, stopwatch, phase).ConfigureAwait(false);
        if (buildResult != null)
        {
            return buildResult;
        }

        var postBuildResult = await RunSubsystemPostBuildAsync(subsystem).ConfigureAwait(false);
        if (postBuildResult != null)
        {
            return postBuildResult;
        }

        var testRunTrxPaths = new List<(string projectName, string trxPath)>();
        var testsRan = _context.RunTests && subsystem.Test?.Projects.Count > 0;

        var testResult = await RunSubsystemTestsAsync(subsystem, stateManager, stopwatch, phase, testRunTrxPaths).ConfigureAwait(false);
        if (testResult != null)
        {
            return testResult;
        }

        var benchmarkResult = await RunSubsystemBenchmarksAsync(subsystem, stateManager, stopwatch, phase).ConfigureAwait(false);
        if (benchmarkResult != null)
        {
            return benchmarkResult;
        }

        RecordSubsystemTelemetry(subsystem, stateManager, stopwatch, testsRan, testRunTrxPaths);

        _presenter.SubsystemSummary(subsystem, stopwatch.Elapsed.TotalSeconds, _context.RunTests);

        return BuildResult.Success(artifacts, []);
    }

    private async Task<SubsystemDefinition?> DiscoverSubsystemAsync(
        string subsystemName,
        SubsystemDiscoverer discoverer,
        PhaseCounter phase)
    {
        _presenter.SubsystemHeader(subsystemName);

        _presenter.Phase(phase.Next(), "Discovering subsystem...");
        var subsystem = await discoverer.GetByNameAsync(subsystemName).ConfigureAwait(false);

        if (subsystem == null)
        {
            return null;
        }

        _presenter.SubsystemFound(subsystem);
        return subsystem;
    }

    private void PreviewSubsystemDryRun(SubsystemDefinition subsystem)
    {
        IReadOnlyList<PreBuildStep>? steps = null;
        var platform = _platform.GetCurrentPlatform();

        if (subsystem.PreBuild != null)
        {
            steps = platform switch
            {
                "linux" => subsystem.PreBuild.Linux,
                "windows" => subsystem.PreBuild.Windows,
                "macos" => subsystem.PreBuild.MacOS,
                _ => null
            };
        }

        _presenter.SubsystemDryRun(subsystem, platform, steps, _context.RunTests);
    }

    private async Task<BuildResult?> BuildSubsystemDependenciesAsync(
        SubsystemDefinition subsystem,
        SubsystemDiscoverer discoverer,
        BuildStateManager stateManager,
        PhaseCounter phase)
    {
        if (subsystem.Dependencies.Count == 0)
        {
            return null;
        }

        _presenter.Phase(phase.Next(), "Building dependencies...");

        foreach (var depName in subsystem.Dependencies)
        {
            var dep = await discoverer.GetByNameAsync(depName).ConfigureAwait(false);
            if (dep?.SolutionPath == null)
            {
                continue;
            }

            if (_context.IncrementalBuild && !stateManager.IsSubsystemStale(dep))
            {
                _presenter.DependencyUpToDate(depName);
                continue;
            }

            var depResult = await _shell.BuildSolutionAsync(
                dep.SolutionPath,
                dep.BuildConfiguration,
                dep.Name).ConfigureAwait(false);

            if (!depResult)
            {
                stateManager.RecordBuild(dep, false);
                stateManager.SaveState();
                return BuildResult.Failure($"Failed to build dependency: {depName}");
            }

            _presenter.DependencyBuilt(depName);
            stateManager.RecordBuild(dep, true);
        }

        _presenter.Newline();
        return null;
    }

    private async Task<BuildResult?> RunSubsystemPreBuildAsync(
        SubsystemDefinition subsystem,
        PhaseCounter phase)
    {
        if (subsystem.PreBuild == null)
        {
            return null;
        }

        _presenter.Phase(phase.Next(), "Executing pre-build steps...");

        var preBuildSuccess = await _hooks.ExecutePreBuildStepsAsync(subsystem).ConfigureAwait(false);
        if (!preBuildSuccess)
        {
            return BuildResult.Failure($"Pre-build steps failed for {subsystem.Name}");
        }

        _presenter.Newline();
        return null;
    }

    private async Task<BuildResult?> BuildSubsystemSolutionAsync(
        SubsystemDefinition subsystem,
        BuildStateManager stateManager,
        Stopwatch stopwatch,
        PhaseCounter phase)
    {
        var buildPhase = phase.Next();

        if (_context.IncrementalBuild && !stateManager.IsSubsystemStale(subsystem))
        {
            _presenter.SubsystemUpToDate(subsystem.Name, stopwatch.Elapsed.TotalSeconds);
            stateManager.RecordBuild(subsystem, true);
            stateManager.SaveState();
            return BuildResult.Success([], []);
        }

        _presenter.Phase(buildPhase, $"Building {subsystem.Name}...");

        if (subsystem.SolutionPath == null)
        {
            _presenter.NoSolutionWarning();
        }
        else
        {
            var buildSuccess = await _shell.BuildSolutionAsync(
                subsystem.SolutionPath,
                subsystem.BuildConfiguration,
                subsystem.Name).ConfigureAwait(false);

            if (!buildSuccess)
            {
                stateManager.RecordBuild(subsystem, false);
                stateManager.RecordBuildTelemetry(subsystem.Name, stopwatch.Elapsed.TotalSeconds, null, null);
                stateManager.SaveState();
                return BuildResult.Failure($"Build failed for {subsystem.Name}");
            }

            _presenter.SubsystemBuilt(subsystem.Name);
        }

        _presenter.Newline();
        return null;
    }

    private async Task<BuildResult?> RunSubsystemPostBuildAsync(SubsystemDefinition subsystem)
    {
        if (subsystem.PostBuild == null)
        {
            return null;
        }

        var hookSuccess = await _hooks.ExecutePostBuildHooksAsync(subsystem).ConfigureAwait(false);
        if (!hookSuccess)
        {
            return BuildResult.Failure("Post-build hooks failed");
        }

        _presenter.Newline();
        return null;
    }

    private async Task<BuildResult?> RunSubsystemTestsAsync(
        SubsystemDefinition subsystem,
        BuildStateManager stateManager,
        Stopwatch stopwatch,
        PhaseCounter phase,
        List<(string projectName, string trxPath)> testRunTrxPaths)
    {
        var test = subsystem.Test;
        if (!_context.RunTests
            || test == null
            || test.Projects.Count == 0)
        {
            return null;
        }

        _presenter.Phase(phase.Next(), "Running tests...");

        var allTestsPassed = true;
        var testLogs = new List<string>();
        var testRunId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff");

        foreach (var testProject in test.Projects)
        {
            var testPath = Path.Combine(subsystem.Directory, testProject, $"{testProject}.csproj");
            if (File.Exists(testPath))
            {
                var (testSuccess, logPath, trxPath) = await _shell.RunTestsWithLoggingAsync(
                    testPath,
                    subsystem.Name,
                    testProject,
                    testRunId,
                    test.Output,
                    test.Coverage).ConfigureAwait(false);

                testLogs.Add(logPath);
                testRunTrxPaths.Add((testProject, trxPath));

                if (testSuccess)
                {
                    _presenter.TestPassed(testProject);
                }
                else
                {
                    _presenter.TestFailed(testProject);
                    allTestsPassed = false;
                }
            }
            else
            {
                _presenter.TestNotFound(testProject);
            }
        }

        if (subsystem.PostTest != null)
        {
            var hookSuccess = await _hooks.ExecutePostTestHooksAsync(subsystem, testLogs).ConfigureAwait(false);
            if (!hookSuccess)
            {
                return BuildResult.Failure("Post-test hooks failed");
            }
        }

        if (!allTestsPassed)
        {
            var failedAggregator = new TestResultAggregator(_context);
            var failedTestResults = TestResultAggregator.AggregateTestResults(testRunTrxPaths);
            var failedCoverageMetrics = !_context.DisableCoverage && subsystem.Test?.Coverage?.Enabled != false
                ? failedAggregator.AggregateLatestCoverage(subsystem.Name)
                : null;

            stateManager.RecordBuildTelemetry(subsystem.Name, stopwatch.Elapsed.TotalSeconds, failedTestResults, failedCoverageMetrics);
            stateManager.SaveState();

            var latestLog = Path.Combine(_context.LogDirectory, $"test-{subsystem.Name}-latest.log");
            return BuildResult.Failure($"Some tests failed. See: {latestLog}");
        }

        _presenter.Newline();
        return null;
    }

    private async Task<BuildResult?> RunSubsystemBenchmarksAsync(
        SubsystemDefinition subsystem,
        BuildStateManager stateManager,
        Stopwatch stopwatch,
        PhaseCounter phase)
    {
        var benchmark = subsystem.Benchmark;
        if (!_context.RunBenchmarks
            || benchmark?.Project == null)
        {
            return null;
        }

        _presenter.Phase(phase.Next(), "Running benchmarks...");

        var benchmarkPath = Path.Combine(subsystem.Directory, benchmark.Project, $"{benchmark.Project}.csproj");
        if (File.Exists(benchmarkPath))
        {
            var (benchSuccess, outputPath) = await _shell.RunBenchmarkWithPersistenceAsync(
                benchmarkPath,
                subsystem.Name,
                benchmark.Output).ConfigureAwait(false);

            if (benchSuccess)
            {
                _presenter.BenchmarkPassed(benchmark.Project, outputPath);

                if (!_context.SkipValidation)
                {
                    var validation = await ValidateBenchmarksAsync(subsystem, outputPath).ConfigureAwait(false);
                    if (!validation.IsSuccess)
                    {
                        stateManager.RecordBuild(subsystem, false);
                        stateManager.RecordBuildTelemetry(subsystem.Name, stopwatch.Elapsed.TotalSeconds, null, null);
                        stateManager.SaveState();
                        return validation;
                    }
                }
            }
            else
            {
                _presenter.BenchmarkFailed(benchmark.Project);
            }
        }
        else
        {
            _presenter.BenchmarkNotFound(benchmark.Project);
        }

        _presenter.Newline();
        return null;
    }

    private void RecordSubsystemTelemetry(
        SubsystemDefinition subsystem,
        BuildStateManager stateManager,
        Stopwatch stopwatch,
        bool testsRan,
        List<(string projectName, string trxPath)> testRunTrxPaths)
    {
        stateManager.RecordBuild(subsystem, true);

        var buildDuration = stopwatch.Elapsed.TotalSeconds;
        var aggregator = new TestResultAggregator(_context);

        TestResults? testResults = null;
        if (testsRan)
        {
            testResults = TestResultAggregator.AggregateTestResults(testRunTrxPaths);
        }

        CoverageMetrics? coverageMetrics = null;
        if (_context.RunTests && !_context.DisableCoverage
            && subsystem.Test?.Coverage?.Enabled != false)
        {
            coverageMetrics = aggregator.AggregateLatestCoverage(subsystem.Name);
        }

        stateManager.RecordBuildTelemetry(subsystem.Name, buildDuration, testResults, coverageMetrics);
        stateManager.SaveState();
    }

    private async Task<BuildResult> ValidateBenchmarksAsync(SubsystemDefinition subsystem, string benchmarkOutputPath)
    {
        try
        {
            var baselineManager = new BaselineManager(_context);
            var jsonlPath = Path.Combine(benchmarkOutputPath, "results.jsonl");
            if (!File.Exists(jsonlPath))
            {
                var baseline = await baselineManager.LoadBaselineAsync(subsystem.Name).ConfigureAwait(false);
                if (baseline is null)
                {
                    if (_context.Verbose)
                    {
                        AnsiConsole.MarkupLine($"[dim]  No benchmark results found at {jsonlPath} and no registered baseline; nothing to compare[/]");
                    }
                    return BuildResult.Success([], []);
                }

                AnsiConsole.MarkupLine($"[red]  No benchmark results produced at {jsonlPath.EscapeMarkup()} for {subsystem.Name}, but a baseline is registered; treating as a gate failure.[/]");

                if (_context.AllowBenchmarkRegression)
                {
                    AnsiConsole.MarkupLine("[yellow]  Allowed via --allow-benchmark-regression; not failing the build.[/]");
                    return BuildResult.Success([], []);
                }

                return BuildResult.Failure($"Benchmark run for {subsystem.Name} produced no results but a baseline is registered");
            }

            var currentResults = BenchmarkResultReader.FromFile(jsonlPath, subsystem.Name);
            var comparison = await baselineManager.CompareToBaselineAsync(subsystem.Name, currentResults).ConfigureAwait(false);

            AnsiConsole.WriteLine();
            baselineManager.DisplayComparison(comparison);

            if (comparison.PlatformMismatch)
            {
                if (_context.AllowBenchmarkRegression)
                {
                    AnsiConsole.MarkupLine("[yellow]  Platform mismatch allowed via --allow-benchmark-regression; not failing the build.[/]");
                    return BuildResult.Success([], []);
                }

                return BuildResult.Failure($"Benchmark baseline platform mismatch for {subsystem.Name}; cannot compare");
            }

            if (!comparison.HasRegressions)
            {
                return BuildResult.Success([], []);
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[red]──────────────────────────────────────────────────────────[/]");
            AnsiConsole.MarkupLine($"[red]Benchmark regressions detected for {subsystem.Name} (see above)[/]");
            AnsiConsole.MarkupLine("[red]──────────────────────────────────────────────────────────[/]");

            if (_context.AllowBenchmarkRegression)
            {
                AnsiConsole.MarkupLine("[yellow]  Allowed via --allow-benchmark-regression; not failing the build.[/]");
                return BuildResult.Success([], []);
            }

            AnsiConsole.MarkupLine($"[dim]  To accept intentional baseline bumps: smash baseline update {subsystem.Name} --approve[/]");
            AnsiConsole.MarkupLine("[dim]  Or rerun with --allow-benchmark-regression to bypass the gate.[/]");
            return BuildResult.Failure($"Benchmark regressions detected for {subsystem.Name}");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]  Benchmark comparison failed for {subsystem.Name}: {ex.Message.EscapeMarkup()}[/]");

            if (_context.AllowBenchmarkRegression)
            {
                AnsiConsole.MarkupLine("[yellow]  Allowed via --allow-benchmark-regression; not failing the build.[/]");
                return BuildResult.Success([], []);
            }

            return BuildResult.Failure($"Benchmark comparison failed for {subsystem.Name}: {ex.Message}");
        }
    }
}
