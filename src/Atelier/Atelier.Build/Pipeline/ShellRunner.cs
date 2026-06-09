using Atelier.Build.Discovery;
using Atelier.Build.MetaOptimization;
using Atelier.Build.Utils;
using Spectre.Console;

namespace Atelier.Build.Pipeline;

public sealed class ShellRunner
{
    private readonly BuildContext _context;
    private readonly TestLogStore _testLogStore;

    public ShellRunner(BuildContext context)
    {
        _context = context;
        _testLogStore = new TestLogStore(context);
    }

    public async Task<bool> BuildSolutionAsync(
        string solutionPath,
        string configuration,
        string subsystemName)
    {
        var args = new List<string>
        {
            "build",
            solutionPath,
            "-c",
            configuration
        };

        if (!_context.Verbose)
        {
            args.Add("-v");
            args.Add("q");
            args.Add("--nologo");
        }

        var executor = new ProcessExecutor(_context);
        try
        {
            var options = _context.Verbose
                ? ProcessOptions.WithTimeoutAndCallbacks(
                    _context.Timeouts.DotnetBuild,
                    onOutputLine: line => AnsiConsole.Write(new Text(line + Environment.NewLine, new Style(decoration: Decoration.Dim))))
                : ProcessOptions.WithTimeout(_context.Timeouts.DotnetBuild);

            var result = await executor.ExecuteAsync(
                "dotnet",
                args,
                Path.GetDirectoryName(solutionPath)!,
                options,
                CancellationToken.None).ConfigureAwait(false);

            if (!result.Success)
            {
                if (!string.IsNullOrWhiteSpace(result.StandardError))
                {
                    AnsiConsole.Write(new Text(result.StandardError, new Style(Color.Red)));
                    AnsiConsole.WriteLine();
                }
                return false;
            }

            return true;
        }
        catch (ProcessExecutionException ex)
        {
            AnsiConsole.Write(new Text(ex.Message, new Style(Color.Red)));
            AnsiConsole.WriteLine();
            return false;
        }
    }

    public async Task<(bool success, string logPath, string trxPath)> RunTestsWithLoggingAsync(
        string projectPath,
        string subsystemName,
        string projectName,
        string runId,
        TestOutputConfig? outputConfig,
        CoverageConfig? coverageConfig)
    {
        var testResultsDir = outputConfig?.Directory ??
            Path.Combine(_context.TestResultsDirectory, subsystemName);
        Directory.CreateDirectory(testResultsDir);

        var trxPath = Path.Combine(testResultsDir, $"{projectName}-{runId}.trx");

        var args = new List<string>
        {
            "test",
            projectPath,
            "--no-build",
            "--logger",
            $"trx;LogFileName={trxPath}"
        };

        if (outputConfig?.Loggers != null)
        {
            foreach (var logger in outputConfig.Loggers.Where(l => !l.Equals("trx", StringComparison.OrdinalIgnoreCase)))
            {
                var loggerPath = Path.Combine(testResultsDir,
                    $"{projectName}-{DateTime.Now:yyyyMMdd-HHmmss}.{logger}");
                args.Add("--logger");
                args.Add($"{logger};LogFileName={loggerPath}");
            }
        }

        var enableCoverage = !_context.DisableCoverage && (coverageConfig?.Enabled ?? true);
        string? coverageFilePath = null;

        if (enableCoverage)
        {
            var coverageCollector = new CoverageCollector(_context);
            var coverageArgs = coverageCollector.GenerateCoverageArguments(
                subsystemName, projectName, coverageConfig, out coverageFilePath);
            args.AddRange(coverageArgs);
        }

        var executor = new ProcessExecutor(_context);
        ProcessResult result;

        try
        {
            result = await executor.ExecuteAsync(
                "dotnet",
                args,
                Path.GetDirectoryName(projectPath)!,
                ProcessOptions.WithTimeout(_context.Timeouts.DotnetTest),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (ProcessExecutionException ex)
        {
            var logPath = await _testLogStore.WriteTestLogAsync(
                subsystemName,
                projectName,
                ex.Message,
                ex.ExitCode,
                "normal").ConfigureAwait(false);
            return (false, logPath, trxPath);
        }

        var combinedOutput = $"{result.StandardOutput}\n{result.StandardError}".Trim();
        var success = result.Success;

        var verbosity = success ? "quiet" : "normal";
        var finalLogPath = await _testLogStore.WriteTestLogAsync(
            subsystemName,
            projectName,
            success ? "Tests passed" : combinedOutput,
            result.ExitCode,
            verbosity).ConfigureAwait(false);

        if ((_context.Verbose || !success) && !string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            AnsiConsole.Write(new Text(result.StandardOutput, new Style(decoration: Decoration.Dim)));
            AnsiConsole.WriteLine();
        }

        if (enableCoverage && success
            && coverageFilePath != null)
        {
            var coverageCollector = new CoverageCollector(_context);
            var coverageDir = Path.GetDirectoryName(coverageFilePath)!;

            var summary = coverageCollector.ParseCoverageSummary(coverageDir);
            if (summary != null)
            {
                coverageCollector.DisplayCoverageSummary(projectName, summary, coverageConfig);
            }

            if (coverageConfig?.HtmlReport ?? true)
            {
                var htmlPath = await coverageCollector.GenerateHtmlReportAsync(subsystemName, coverageDir).ConfigureAwait(false);
                if (htmlPath != null)
                {
                    AnsiConsole.MarkupLine("  [dim]HTML report:[/]");
                    AnsiConsole.WriteLine($"    {htmlPath}");
                }
            }
        }

        return (success, finalLogPath, trxPath);
    }

    public async Task<(bool success, string outputPath)> RunBenchmarkWithPersistenceAsync(
        string projectPath,
        string subsystemName,
        BenchmarkOutputConfig? outputConfig)
    {
        var benchmarkDir = outputConfig?.Directory ??
            Path.Combine(_context.BenchmarkResultsDirectory, subsystemName, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"));
        Directory.CreateDirectory(benchmarkDir);

        var args = new List<string>
        {
            "run",
            "-c",
            "Release",
            "--project",
            projectPath
        };

        var executor = new ProcessExecutor(_context);
        ProcessResult result;

        try
        {
            var options = _context.Verbose
                ? ProcessOptions.WithTimeoutAndCallbacks(
                    _context.Timeouts.Benchmarks,
                    onOutputLine: line => AnsiConsole.MarkupLine($"[dim]{line}[/]"))
                : ProcessOptions.WithTimeout(_context.Timeouts.Benchmarks);

            result = await executor.ExecuteAsync(
                "dotnet",
                args,
                Path.GetDirectoryName(projectPath)!,
                options,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (ProcessExecutionException)
        {
            return (false, benchmarkDir);
        }

        var jsonlPath = Path.Combine(benchmarkDir, "results.jsonl");
        BenchmarkResultReader.WriteJsonl(result.StandardOutputLines, jsonlPath);

        var latestDir = Path.Combine(_context.BenchmarkResultsDirectory, subsystemName, "latest");
        if (Directory.Exists(latestDir))
        {
            try
            {
                Directory.Delete(latestDir, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (_context.Verbose)
                {
                    AnsiConsole.MarkupLine($"  [dim]Could not delete benchmark latest dir {latestDir.EscapeMarkup()}: {ex.Message.EscapeMarkup()}[/]");
                }
            }
        }
        Directory.CreateDirectory(latestDir);
        File.Copy(jsonlPath, Path.Combine(latestDir, "results.jsonl"), overwrite: true);

        return (result.Success, benchmarkDir);
    }
}
