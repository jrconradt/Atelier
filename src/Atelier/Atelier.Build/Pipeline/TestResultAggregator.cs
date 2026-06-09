namespace Atelier.Build.Pipeline;

public sealed class TestResultAggregator
{
    private readonly BuildContext _context;

    public TestResultAggregator(BuildContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public static TestResults? AggregateTestResults(IReadOnlyList<(string projectName, string trxPath)> trxResults)
    {
        var testResults = new TestResults
        {
            Projects = new Dictionary<string, TestProjectResult>()
        };

        foreach (var (projectName, trxPath) in trxResults)
        {
            var projectResult = TrxResultReader.Read(trxPath);
            if (projectResult is null)
            {
                continue;
            }

            testResults.Projects[projectName] = projectResult;
            testResults.Total += projectResult.Total;
            testResults.Passed += projectResult.Passed;
            testResults.Failed += projectResult.Failed;
            testResults.Skipped += projectResult.Skipped;
            testResults.Duration += projectResult.Duration;
        }

        return testResults.Total > 0 ? testResults : null;
    }

    public CoverageMetrics? AggregateLatestCoverage(string subsystemName)
    {
        var coverageDir = Path.Combine(_context.CoverageOutputDirectory, subsystemName);
        if (!Directory.Exists(coverageDir))
        {
            return null;
        }

        var projectDirs = Directory.GetDirectories(coverageDir);
        if (projectDirs.Length == 0)
        {
            return null;
        }

        double totalLinesCovered = 0;
        double totalLines = 0;
        double totalBranchesCovered = 0;
        double totalBranches = 0;

        foreach (var projectDir in projectDirs)
        {
            var timestampDirs = Directory.GetDirectories(projectDir)
                .OrderByDescending(Directory.GetLastWriteTimeUtc)
                .ToArray();

            if (timestampDirs.Length == 0)
            {
                continue;
            }

            var coberturaFile = Path.Combine(timestampDirs[0], "coverage.cobertura.xml");
            if (!File.Exists(coberturaFile))
            {
                continue;
            }

            var totals = CoverageCollector.ReadCoberturaTotals(coberturaFile);
            if (totals is null)
            {
                continue;
            }

            totalLinesCovered += totals.LinesCovered;
            totalLines += totals.LinesValid;
            totalBranchesCovered += totals.BranchesCovered;
            totalBranches += totals.BranchesValid;
        }

        if (totalLines == 0)
        {
            return null;
        }

        return new CoverageMetrics
        {
            LineRate = (totalLinesCovered / totalLines) * 100,
            BranchRate = totalBranches > 0 ? (totalBranchesCovered / totalBranches) * 100 : 0,
            LinesCovered = (int)totalLinesCovered,
            LinesTotal = (int)totalLines,
            BranchesCovered = (int)totalBranchesCovered,
            BranchesTotal = (int)totalBranches
        };
    }
}
