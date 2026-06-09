using System.Globalization;

namespace Atelier.Build.MetaOptimization;

public sealed class BaselineComparison
{
        public required string Subsystem { get; init; }

        public required DateTime BaselineTimestamp { get; init; }

        public required DateTime CurrentTimestamp { get; init; }

        public List<BenchmarkDelta> Regressions { get; init; } = [];

        public List<BenchmarkDelta> Improvements { get; init; } = [];

        public List<BenchmarkDelta> Stable { get; init; } = [];

        public List<string> NewBenchmarks { get; init; } = [];

        public List<string> RemovedBenchmarks { get; init; } = [];

        public bool PlatformMismatch { get; init; }

        public int TotalCompared => Regressions.Count + Improvements.Count + Stable.Count;

        public bool HasRegressions => Regressions.Count > 0;

        public bool HasImprovements => Improvements.Count > 0;
}

public sealed class BenchmarkDelta
{
        public required string BenchmarkName { get; init; }

        public required string Category { get; init; }

        public required double BaselineMean { get; init; }

        public required double CurrentMean { get; init; }

        public required double BaselineStdDev { get; init; }

        public required double CurrentStdDev { get; init; }

        public required double PercentChange { get; init; }

        public required bool IsSignificant { get; init; }

        public required double Tolerance { get; init; }

        public required long BaselineAllocated { get; init; }

        public required long CurrentAllocated { get; init; }

        public required double AllocatedPercentChange { get; init; }

        public required bool AllocationRegressed { get; init; }

        public List<string> Tags { get; init; } = [];

        public double AbsoluteChange => CurrentMean - BaselineMean;

        public string Summary
    {
        get
        {
            var sign = PercentChange > 0 ? "+" : string.Empty;
            var status = PercentChange > 0 ? "slower" : "faster";
            var baseline = BaselineMean.ToString("F2", CultureInfo.InvariantCulture);
            var current = CurrentMean.ToString("F2", CultureInfo.InvariantCulture);
            var percent = PercentChange.ToString("F1", CultureInfo.InvariantCulture);
            var meanSummary = $"{BenchmarkName}: {baseline}ns → {current}ns ({sign}{percent}%) {status}";

            if (!AllocationRegressed)
            {
                return meanSummary;
            }

            var allocSign = AllocatedPercentChange > 0 ? "+" : string.Empty;
            var allocPercent = AllocatedPercentChange.ToString("F1", CultureInfo.InvariantCulture);
            return $"{meanSummary}; allocated {BaselineAllocated}B → {CurrentAllocated}B ({allocSign}{allocPercent}%)";
        }
    }
}
