using System.Reflection;
using System.Runtime.Loader;
using Atelier.Framework.Testing;

namespace Atelier.Build.Pipeline;

public sealed record GeneratedTestOptions(
    bool DryRun,
    string? Filter,
    int MaxNeedsFixture,
    string? AllowlistPath);

public sealed record GeneratedTestOutcome(
    int ExitCode,
    int Total,
    int Pass,
    int Fail,
    int NeedsFixture,
    int BudgetBreaches);

public sealed class GeneratedTestHarness
{
    private const int CHECKPOINT_EVERY = 50;
    private const string CHECKPOINT_FILE_NAME = "test-checkpoint.txt";
    private const string DEFAULT_ALLOWLIST_FILE_NAME = "test-nf-allowlist.txt";

    private readonly BuildContext _context;

    public GeneratedTestHarness(BuildContext context)
    {
        _context = context;
    }

    public async Task<GeneratedTestOutcome> RunAsync(GeneratedTestOptions options)
    {
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            if (_context.Verbose)
            {
                Console.WriteLine($"[runner] unobserved: {e.Exception.GetType().Name}: {e.Exception.Message}");
            }
            e.SetObserved();
        };

        var assemblies = LoadFrameworkAssemblies();

        Console.WriteLine($"Atelier Test Runner — {(options.DryRun ? "DISCOVER" : "RUN")}");
        Console.WriteLine($"Loaded {assemblies.Count} framework assemblies from {_context.SolutionRoot}");

        TestFixtures.DiscoverAll(assemblies);
        Console.WriteLine($"Registered {TestFixtures.RegisteredTypes.Count} test fixtures");

        Directory.CreateDirectory(_context.TestResultsDirectory);
        var checkpointPath = Path.Combine(_context.TestResultsDirectory, CHECKPOINT_FILE_NAME);

        int runningPass = 0, runningFail = 0, runningNf = 0, runningTotal = 0;
        var lastTestLabel = "(none)";

        try
        {
            File.Delete(checkpointPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[runner] could not clear checkpoint '{checkpointPath}': {ex.GetType().Name}: {ex.Message}");
        }

        void WriteCheckpoint()
        {
            try
            {
                File.WriteAllText(checkpointPath,
                                  $"Total: {runningTotal}\n  Pass: {runningPass}\n  Fail: {runningFail}\n  NeedsFixture: {runningNf}\n  LastTest: {lastTestLabel}\n");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"[runner] could not write checkpoint '{checkpointPath}': {ex.GetType().Name}: {ex.Message}");
            }
        }

        var report = await AtelierTestRunner.RunAsync(
            assemblies,
            dryRun: options.DryRun,
            filter: options.Filter,
            onResult: r =>
            {
                runningTotal++;
                switch (r.Status)
                {
                    case TestStatus.Pass:
                        runningPass++;
                        break;
                    case TestStatus.Fail:
                        runningFail++;
                        break;
                    case TestStatus.NeedsFixture:
                        runningNf++;
                        break;
                }
                lastTestLabel = $"{r.TypeName}.{r.MethodName} → {r.Status}";
                if (runningTotal % CHECKPOINT_EVERY == 0
                    || r.Status == TestStatus.Fail)
                {
                    WriteCheckpoint();
                }
            }).ConfigureAwait(false);

        WriteCheckpoint();

        RenderReport(report);

        var budget = LoadBudget(options);
        var breaches = RenderBudgetSections(report, budget);

        int exitCode;
        if (options.DryRun)
        {
            exitCode = report.Fail > 0 ? 1 : 0;
        }
        else
        {
            exitCode = report.Fail > 0 || breaches > 0 ? 1 : 0;
        }

        return new GeneratedTestOutcome(exitCode,
                                        report.Total,
                                        report.Pass,
                                        report.Fail,
                                        report.NeedsFixture,
                                        breaches);
    }

    private List<Assembly> LoadFrameworkAssemblies()
    {
        var seen = new HashSet<string>();
        var dllPaths = new List<string>();
        var resolverMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dll in EnumerateBuildOutputDlls(_context.SolutionRoot))
        {
            if (!IsBuildOutput(dll))
            {
                continue;
            }

            var fileName = Path.GetFileName(dll);
            var simpleName = Path.GetFileNameWithoutExtension(dll);
            if (!resolverMap.ContainsKey(simpleName))
            {
                resolverMap[simpleName] = dll;
            }

            if (!fileName.StartsWith("Atelier.Framework.", StringComparison.Ordinal))
            {
                continue;
            }
            if (fileName.EndsWith(".Generators.dll", StringComparison.Ordinal))
            {
                continue;
            }
            if (!seen.Add(fileName))
            {
                continue;
            }
            dllPaths.Add(dll);
        }

        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            if (name.Name is { } simple && resolverMap.TryGetValue(simple, out var path))
            {
                try
                {
                    return ctx.LoadFromAssemblyPath(path);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[runner] failed to resolve '{simple}' from '{path}': {ex.GetType().Name}: {ex.Message}");
                    return null;
                }
            }
            return null;
        };

        var assemblies = new List<Assembly>();
        foreach (var path in dllPaths)
        {
            try
            {
                assemblies.Add(Assembly.LoadFrom(path));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[runner] failed to load assembly '{path}': {ex.GetType().Name}: {ex.Message}");
            }
        }

        return assemblies;
    }

    private static IEnumerable<string> EnumerateBuildOutputDlls(string solutionRoot)
    {
        foreach (var binDir in Directory.EnumerateDirectories(solutionRoot, "bin", SearchOption.AllDirectories))
        {
            foreach (var configName in new[] { "Debug", "Release" })
            {
                var configDir = Path.Combine(binDir, configName);
                if (!Directory.Exists(configDir))
                {
                    continue;
                }
                foreach (var dll in Directory.EnumerateFiles(configDir, "*.dll", SearchOption.AllDirectories))
                {
                    yield return dll;
                }
            }
        }
    }

    private static bool IsBuildOutput(string path)
    {
        return path.Contains("/bin/Debug/", StringComparison.Ordinal)
            || path.Contains("/bin/Release/", StringComparison.Ordinal)
            || path.Contains("\\bin\\Debug\\", StringComparison.Ordinal)
            || path.Contains("\\bin\\Release\\", StringComparison.Ordinal);
    }

    private NeedsFixtureBudget LoadBudget(GeneratedTestOptions options)
    {
        var allowlistPath = options.AllowlistPath;
        if (string.IsNullOrEmpty(allowlistPath))
        {
            var defaultPath = Path.Combine(_context.SolutionRoot, DEFAULT_ALLOWLIST_FILE_NAME);
            if (File.Exists(defaultPath))
            {
                allowlistPath = defaultPath;
            }
        }

        var perAssembly = new Dictionary<string, int>(StringComparer.Ordinal);
        if (allowlistPath is { Length: > 0 } && File.Exists(allowlistPath))
        {
            foreach (var raw in File.ReadAllLines(allowlistPath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }
                var sep = line.IndexOf('=');
                if (sep <= 0)
                {
                    continue;
                }
                var name = line[..sep].Trim();
                var value = line[(sep + 1)..].Trim();
                if (name.Length > 0 && int.TryParse(value, out var ceiling))
                {
                    perAssembly[name] = ceiling;
                }
            }
        }

        return new NeedsFixtureBudget(options.MaxNeedsFixture, perAssembly);
    }

    private void RenderReport(TestReport report)
    {
        Console.WriteLine();
        Console.WriteLine($"Total:        {report.Total}");
        Console.WriteLine($"  Pass:         {report.Pass}");
        Console.WriteLine($"  Fail:         {report.Fail}");
        Console.WriteLine($"  NeedsFixture: {report.NeedsFixture}");
        Console.WriteLine();

        Console.WriteLine("=== Coverage classification ===");
        Console.WriteLine($"  Structural (wiring/surface conformance) total={report.Structural.Total,4} pass={report.Structural.Pass,4} fail={report.Structural.Fail,4} nf={report.Structural.NeedsFixture,4}");
        Console.WriteLine($"  Behavioral (operation invocation)        total={report.Behavioral.Total,4} pass={report.Behavioral.Pass,4} fail={report.Behavioral.Fail,4} nf={report.Behavioral.NeedsFixture,4}");
        Console.WriteLine("  Structural passes are wiring-conformance signals, not behavioral verification.");
        Console.WriteLine();

        Console.WriteLine("=== Breakdown by invariant ===");
        foreach (var group in report.ByInvariant.OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var p = group.Count(r => r.Status == TestStatus.Pass);
            var f = group.Count(r => r.Status == TestStatus.Fail);
            var n = group.Count(r => r.Status == TestStatus.NeedsFixture);
            Console.WriteLine($"  {group.Key,-60} pass={p,4} fail={f,4} nf={n,4}");
        }
        Console.WriteLine();

        Console.WriteLine("=== Breakdown by assembly ===");
        foreach (var group in report.ByAssembly.OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var p = group.Count(r => r.Status == TestStatus.Pass);
            var f = group.Count(r => r.Status == TestStatus.Fail);
            var n = group.Count(r => r.Status == TestStatus.NeedsFixture);
            Console.WriteLine($"  {group.Key,-60} pass={p,4} fail={f,4} nf={n,4}");
        }
        Console.WriteLine();

        if (report.Fail > 0)
        {
            Console.WriteLine("=== Failures ===");
            foreach (var f in report.Failures)
            {
                Console.WriteLine($"  [{f.Invariant}] {f.TypeName}.{f.MethodName}");
                Console.WriteLine($"      target:    {f.Target}");
                Console.WriteLine($"      exception: {f.ExceptionType}");
                Console.WriteLine($"      detail:    {f.Detail}");
            }
        }

        if (_context.Verbose && report.NeedsFixture > 0)
        {
            Console.WriteLine("=== NeedsFixture ===");
            foreach (var n in report.Needs)
            {
                Console.WriteLine($"  [{n.Invariant}] {n.TypeName}.{n.MethodName}");
                Console.WriteLine($"      target: {n.Target}");
                Console.WriteLine($"      detail: {n.Detail}");
            }
        }
    }

    private static int RenderBudgetSections(TestReport report, NeedsFixtureBudget budget)
    {
        var coverageDebt = report.CoverageDebt(budget);
        if (coverageDebt.Count > 0)
        {
            Console.WriteLine("=== Coverage debt (NeedsFixture by assembly) ===");
            foreach (var debt in coverageDebt)
            {
                Console.WriteLine($"  {debt.AssemblyName,-60} nf={debt.NeedsFixtureCount,4} ceiling={debt.Ceiling,4} allowlisted={debt.Allowlisted,4}");
            }
            Console.WriteLine();
        }

        var uncovered = report.UncoveredRequisiteOperations();
        if (uncovered.Count > 0)
        {
            Console.WriteLine($"=== Untested operations (NeedsFixture, no passing coverage): {uncovered.Count} ===");
            foreach (var op in uncovered)
            {
                Console.WriteLine($"  [{op.Invariant}] {op.TypeName}.{op.MethodName}");
                Console.WriteLine($"      target: {op.Target}");
                Console.WriteLine($"      detail: {op.Detail}");
            }
            Console.WriteLine();
        }

        var breaches = report.BudgetBreaches(budget);
        if (breaches.Count > 0)
        {
            Console.WriteLine("=== NeedsFixture budget exceeded ===");
            foreach (var breach in breaches)
            {
                Console.WriteLine($"  {breach.AssemblyName,-60} nf={breach.Count,4} ceiling={breach.Ceiling,4}");
            }
            Console.WriteLine();
        }

        return breaches.Count;
    }
}
