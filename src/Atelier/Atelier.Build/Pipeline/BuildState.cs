namespace Atelier.Build.Pipeline;

public class BuildState
{
    public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        public Dictionary<string, SubsystemBuildState> Subsystems { get; set; } = [];

        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

public class SubsystemBuildState
{
        public string Name { get; set; } = string.Empty;

        public DateTime LastBuildTime { get; set; }

        public double BuildDuration { get; set; }

        public Dictionary<string, long> SourceFileTicks { get; set; } = [];

        public Dictionary<string, long> OutputFileTicks { get; set; } = [];

        public List<string> Dependencies { get; set; } = [];

        public bool BuildSucceeded { get; set; }

        public string Configuration { get; set; } = "Debug";

        public TestResults? TestResults { get; set; }

        public CoverageMetrics? Coverage { get; set; }

        public List<BuildHistoryEntry> History { get; set; } = [];
}

public class TestResults
{
        public int Total { get; set; }

        public int Passed { get; set; }

        public int Failed { get; set; }

        public int Skipped { get; set; }

        public double Duration { get; set; }

        public Dictionary<string, TestProjectResult> Projects { get; set; } = [];
}

public class TestProjectResult
{
    public int Total { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public double Duration { get; set; }
}

public class CoverageMetrics
{
        public double LineRate { get; set; }

        public double BranchRate { get; set; }

        public int LinesCovered { get; set; }

        public int LinesTotal { get; set; }

        public int BranchesCovered { get; set; }

        public int BranchesTotal { get; set; }
}

public class BuildHistoryEntry
{
    public DateTime Timestamp { get; set; }
    public double Duration { get; set; }
    public bool Succeeded { get; set; }
    public string Configuration { get; set; } = "Debug";
    public TestResults? TestResults { get; set; }
    public CoverageMetrics? Coverage { get; set; }
}
