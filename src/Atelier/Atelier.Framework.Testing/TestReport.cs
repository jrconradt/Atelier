namespace Atelier.Framework.Testing;

public enum TestStatus
{
    Pass,
    Fail,
    NeedsFixture,
}

public enum InvariantKind
{
    Structural,
    Behavioral,
}

public static class InvariantClassifier
{
    public static InvariantKind Classify(string invariant)
    {
        if (invariant.StartsWith("DI-Wiring/", StringComparison.Ordinal)
            || invariant.StartsWith("IAtelier/", StringComparison.Ordinal))
        {
            return InvariantKind.Structural;
        }
        return InvariantKind.Behavioral;
    }
}

public sealed record TestResult(
    string AssemblyName,
    string TypeName,
    string MethodName,
    string Invariant,
    string Target,
    TestStatus Status,
    string? Detail = null,
    string? ExceptionType = null);

public sealed record TestReport(
    int Total,
    int Pass,
    int Fail,
    int NeedsFixture,
    IReadOnlyList<TestResult> Results)
{
    public bool AllGreen => Fail == 0;

    public CoverageClassification Structural => Classify(InvariantKind.Structural);

    public CoverageClassification Behavioral => Classify(InvariantKind.Behavioral);

    private CoverageClassification Classify(InvariantKind kind)
    {
        int total = 0, pass = 0, fail = 0, nf = 0;
        foreach (var r in Results)
        {
            if (InvariantClassifier.Classify(r.Invariant) != kind)
            {
                continue;
            }
            total++;
            switch (r.Status)
            {
                case TestStatus.Pass:
                    pass++;
                    break;
                case TestStatus.Fail:
                    fail++;
                    break;
                case TestStatus.NeedsFixture:
                    nf++;
                    break;
            }
        }
        return new CoverageClassification(kind, total, pass, fail, nf);
    }

    public IEnumerable<TestResult> Failures => Results.Where(r => r.Status == TestStatus.Fail);
    public IEnumerable<TestResult> Needs => Results.Where(r => r.Status == TestStatus.NeedsFixture);

    public IEnumerable<IGrouping<string, TestResult>> ByInvariant =>
        Results.GroupBy(r => r.Invariant);

    public IEnumerable<IGrouping<string, TestResult>> ByAssembly =>
        Results.GroupBy(r => r.AssemblyName);

    public IReadOnlyDictionary<string, int> NeedsFixtureByAssembly =>
        Results
            .Where(r => r.Status == TestStatus.NeedsFixture)
            .GroupBy(r => r.AssemblyName)
            .ToDictionary(g => g.Key, g => g.Count());

    public IReadOnlyList<CoverageDebt> CoverageDebt(NeedsFixtureBudget budget)
    {
        var debts = new List<CoverageDebt>();
        foreach (var entry in NeedsFixtureByAssembly.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            var ceiling = budget.CeilingFor(entry.Key);
            var allowlisted = Math.Min(entry.Value, ceiling);
            debts.Add(new CoverageDebt(entry.Key, entry.Value, ceiling, allowlisted));
        }
        return debts;
    }

    public IReadOnlyList<TestResult> UncoveredRequisiteOperations()
    {
        var covered = Results
            .Where(r => r.Status == TestStatus.Pass)
            .Select(r => $"{r.TypeName}.{r.Target}")
            .ToHashSet(StringComparer.Ordinal);

        return Results
            .Where(r => r.Status == TestStatus.NeedsFixture)
            .Where(r => !covered.Contains($"{r.TypeName}.{r.Target}"))
            .OrderBy(r => r.AssemblyName, StringComparer.Ordinal)
            .ThenBy(r => r.TypeName, StringComparer.Ordinal)
            .ThenBy(r => r.MethodName, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<NeedsFixtureBudgetBreach> BudgetBreaches(NeedsFixtureBudget budget)
    {
        var breaches = new List<NeedsFixtureBudgetBreach>();
        foreach (var entry in NeedsFixtureByAssembly.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            var ceiling = budget.CeilingFor(entry.Key);
            if (entry.Value > ceiling)
            {
                breaches.Add(new NeedsFixtureBudgetBreach(entry.Key, entry.Value, ceiling));
            }
        }
        return breaches;
    }
}

public sealed record CoverageClassification(
    InvariantKind Kind,
    int Total,
    int Pass,
    int Fail,
    int NeedsFixture);

public sealed record NeedsFixtureBudgetBreach(
    string AssemblyName,
    int Count,
    int Ceiling);

public sealed record CoverageDebt(
    string AssemblyName,
    int NeedsFixtureCount,
    int Ceiling,
    int Allowlisted);

public sealed class NeedsFixtureBudget
{
    private readonly int _defaultCeiling;
    private readonly IReadOnlyDictionary<string, int> _perAssembly;

    public NeedsFixtureBudget(
        int defaultCeiling,
        IReadOnlyDictionary<string, int> perAssembly)
    {
        _defaultCeiling = defaultCeiling;
        _perAssembly = perAssembly;
    }

    public int CeilingFor(string assemblyName)
    {
        if (_perAssembly.TryGetValue(assemblyName, out var ceiling))
        {
            return ceiling;
        }
        return _defaultCeiling;
    }
}
