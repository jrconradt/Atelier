using Atelier.Build.Utils;

namespace Atelier.Build.Pipeline;

public sealed record XUnitTestOutcome(
    int ExitCode,
    int Total,
    int Passed,
    int Failed,
    int Skipped);

public sealed class XUnitTestRunner
{
    private readonly BuildContext _context;

    public XUnitTestRunner(BuildContext context)
    {
        _context = context;
    }

    public async Task<XUnitTestOutcome> RunAsync(bool dryRun)
    {
        var projects = DiscoverXUnitProjects();

        Console.WriteLine();
        Console.WriteLine($"xUnit Test Projects — {(dryRun ? "DISCOVER" : "RUN")}");
        Console.WriteLine($"Discovered {projects.Count} xUnit test projects under {_context.SolutionRoot}");

        if (projects.Count == 0
            || dryRun)
        {
            foreach (var project in projects)
            {
                Console.WriteLine($"  {Path.GetFileNameWithoutExtension(project)}");
            }

            return new XUnitTestOutcome(0, 0, 0, 0, 0);
        }

        var resultsDir = Path.Combine(_context.TestResultsDirectory, "xunit");
        Directory.CreateDirectory(resultsDir);

        var executor = new ProcessExecutor(_context);

        var total = 0;
        var passed = 0;
        var failed = 0;
        var skipped = 0;
        var anyFailed = false;

        foreach (var project in projects)
        {
            var projectName = Path.GetFileNameWithoutExtension(project);
            var trxName = $"{projectName}.trx";
            var trxPath = Path.Combine(resultsDir, trxName);

            DeleteStaleResult(trxPath);

            var args = new List<string>
            {
                "test",
                project,
                "--nologo",
                "--results-directory",
                resultsDir,
                "--logger",
                $"trx;LogFileName={trxName}"
            };

            var ran = true;
            var processSucceeded = false;

            try
            {
                var result = await executor.ExecuteAsync("dotnet",
                                                         args,
                                                         Path.GetDirectoryName(project)!,
                                                         ProcessOptions.WithTimeout(_context.Timeouts.DotnetTest),
                                                         CancellationToken.None).ConfigureAwait(false);
                processSucceeded = result.Success;
            }
            catch (ProcessExecutionException)
            {
                ran = false;
            }

            var parsed = TrxResultReader.Read(trxPath);

            if (parsed is null)
            {
                anyFailed = true;
                Console.WriteLine($"  {projectName,-58} pass=   0 fail=   ? skip=   0");
                continue;
            }

            total += parsed.Total;
            passed += parsed.Passed;
            failed += parsed.Failed;
            skipped += parsed.Skipped;

            if (parsed.Failed > 0
                || !ran
                || !processSucceeded)
            {
                anyFailed = true;
            }

            Console.WriteLine($"  {projectName,-58} pass={parsed.Passed,4} fail={parsed.Failed,4} skip={parsed.Skipped,4}");
        }

        Console.WriteLine();
        Console.WriteLine($"xUnit total={total} pass={passed} fail={failed} skip={skipped}");

        return new XUnitTestOutcome(anyFailed ? 1 : 0, total, passed, failed, skipped);
    }

    private static void DeleteStaleResult(string trxPath)
    {
        try
        {
            File.Delete(trxPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[xunit] could not clear stale result '{trxPath}': {ex.GetType().Name}: {ex.Message}");
        }
    }

    private List<string> DiscoverXUnitProjects()
    {
        var projects = new List<string>();

        foreach (var csproj in Directory.EnumerateFiles(_context.SolutionRoot,
                                                        "*.Tests.csproj",
                                                        SearchOption.AllDirectories))
        {
            if (IsBuildArtifactPath(csproj))
            {
                continue;
            }

            var text = File.ReadAllText(csproj);
            if (text.Contains("Include=\"xunit\"", StringComparison.Ordinal))
            {
                projects.Add(csproj);
            }
        }

        projects.Sort(StringComparer.Ordinal);
        return projects;
    }

    private static bool IsBuildArtifactPath(string path)
    {
        return path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }
}
