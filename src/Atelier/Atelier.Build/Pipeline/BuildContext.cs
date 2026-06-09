using Atelier.Build.Utils;

namespace Atelier.Build.Pipeline;

public enum BuildMode
{
    FullBuild,
    DirectProject,
    Subsystem,
    BoutiqueGeneration
}

public class BuildContext
{
    public required string WorkingDirectory { get; init; }
    public string? ProjectPath { get; init; }
    public bool Verbose { get; init; }
    public bool DryRun { get; init; }
    public bool GenerateDiagram { get; init; }
    public bool GenerateBoutiques { get; init; }

        public TimeoutConfiguration Timeouts { get; init; } = TimeoutConfiguration.Default;

        public string? SubsystemName { get; init; }

        public bool RunTests { get; init; }

        public bool RunBenchmarks { get; init; }

        public bool IncrementalBuild { get; init; } = true;

        public bool DisableCoverage { get; init; }

        public bool SkipValidation { get; init; }

        public bool AllowBenchmarkRegression { get; init; }

    public const int MAX_LOG_FILES = 5;

    public bool IsDirectProjectBuild => Mode == BuildMode.DirectProject;
    public bool IsSubsystemBuild => Mode == BuildMode.Subsystem;
    public bool IsBoutiqueGeneration => Mode == BuildMode.BoutiqueGeneration;

    public BuildMode Mode
    {
        get
        {
            if (GenerateBoutiques)
            {
                return BuildMode.BoutiqueGeneration;
            }

            if (SubsystemName != null)
            {
                return BuildMode.Subsystem;
            }

            if (ProjectPath != null)
            {
                return BuildMode.DirectProject;
            }

            return BuildMode.FullBuild;
        }
    }

    private readonly Lazy<string> _solutionRoot;

    public BuildContext()
    {
        _solutionRoot = new Lazy<string>(() => FindSolutionRoot(WorkingDirectory!));
    }

    public string SolutionRoot => _solutionRoot.Value;
    public string BuildOutputDirectory => Path.Combine(SolutionRoot, "Atelier.Build", ".artifacts");
    public string DiagramOutputDirectory => Path.Combine(BuildOutputDirectory, "diagrams");
    public string LogDirectory => Path.Combine(BuildOutputDirectory, "logs");
    public string TestResultsDirectory => Path.Combine(BuildOutputDirectory, "test-results");
    public string BenchmarkResultsDirectory => Path.Combine(BuildOutputDirectory, "benchmarks");
    public string CoverageOutputDirectory => Path.Combine(BuildOutputDirectory, "coverage");
    public string CoverageReportsDirectory => Path.Combine(BuildOutputDirectory, "reports");
    public string BoutiquesDirectory => Path.Combine(SolutionRoot, "boutiques");
    public string BuildStateFilePath => Path.Combine(BuildOutputDirectory, "build-state.json");

    private const int DOWNWARD_SEARCH_MAX_DEPTH = 4;

    private static string FindSolutionRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);

        while (current != null)
        {
            if (HasSolutionMarker(current))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        var descended = FindSolutionRootBelow(startPath);
        if (descended != null)
        {
            return descended;
        }

        throw new InvalidOperationException($"Could not find solution root from {startPath}");
    }

    private static bool HasSolutionMarker(DirectoryInfo directory)
    {
        return directory.GetFiles("*.sln").Length > 0
            || directory.GetFiles("*.slnx").Length > 0
            || directory.GetDirectories("boutiques").Length > 0;
    }

    private static string? FindSolutionRootBelow(string startPath)
    {
        var skip = new HashSet<string>(StringComparer.Ordinal)
        {
            "bin",
            "obj",
            ".git",
            ".artifacts",
            "node_modules",
            "boutiques"
        };

        var frontier = new Queue<(DirectoryInfo Directory, int Depth)>();
        frontier.Enqueue((new DirectoryInfo(startPath), 0));

        while (frontier.Count > 0)
        {
            var (directory, depth) = frontier.Dequeue();

            if (depth > 0
                && HasSolutionMarker(directory))
            {
                return directory.FullName;
            }

            if (depth >= DOWNWARD_SEARCH_MAX_DEPTH)
            {
                continue;
            }

            DirectoryInfo[] children;
            try
            {
                children = directory.GetDirectories();
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in children)
            {
                if (skip.Contains(child.Name))
                {
                    continue;
                }

                frontier.Enqueue((child, depth + 1));
            }
        }

        return null;
    }
}
