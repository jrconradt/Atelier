namespace Atelier.Build.MetaOptimization;

public sealed class BenchmarkResult
{
        public required string Category { get; init; }

        public required string ClassName { get; init; }

        public required string MethodName { get; init; }

        public string? Description { get; init; }

        public bool Baseline { get; init; }

        public required double Mean { get; init; }

        public required double StdDev { get; init; }

        public required long Allocated { get; init; }

        public List<string> Tags { get; init; } = [];

        public string Unit { get; init; } = "ns";

        public double Tolerance { get; init; } = 0.10;

        public string FullName => $"{ClassName}.{MethodName}";
}

public sealed class BenchmarkResults
{
        public required string Subsystem { get; init; }

        public required DateTime Timestamp { get; init; }

        public required Dictionary<string, BenchmarkResult> Results { get; init; }

        public required PlatformInfo Platform { get; init; }
}
