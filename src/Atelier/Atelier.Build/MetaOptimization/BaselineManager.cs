using System.Text.Json;
using System.Text.Json.Serialization;
using Atelier.Build.Pipeline;

namespace Atelier.Build.MetaOptimization;

public sealed class BaselineManager
{
    private const double MINIMUM_SIGNIFICANT_DELTA_NS = 0.5;

    private readonly BuildContext _context;
    private readonly string _baselinesRoot;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public BaselineManager(BuildContext context)
    {
        _context = context;
        _baselinesRoot = Path.Combine(_context.SolutionRoot, ".baselines");
    }

        public async Task<BaselineData?> LoadBaselineAsync(string subsystem)
    {
        var baselinePath = GetBaselinePath(subsystem);
        if (!File.Exists(baselinePath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(baselinePath).ConfigureAwait(false);
            var baseline = JsonSerializer.Deserialize<BaselineData>(json, JsonOptions);
            return baseline;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Failed to load baseline from {baselinePath}: {ex.Message}");
            return null;
        }
    }

        public async Task SaveBaselineAsync(
        string subsystem,
        BenchmarkResults results,
        string updatedBy,
        string changeReason)
    {
        var baselinePath = GetBaselinePath(subsystem);
        var baselineDir = Path.GetDirectoryName(baselinePath)!;

        if (!Directory.Exists(baselineDir))
        {
            Directory.CreateDirectory(baselineDir);
        }

        var existing = await LoadBaselineAsync(subsystem).ConfigureAwait(false);

        var historyEntry = new BaselineHistoryEntry
        {
            Timestamp = DateTime.UtcNow,
            UpdatedBy = updatedBy,
            ChangeReason = changeReason,
            BenchmarksChanged = results.Results.Keys.ToList(),
            CommitHash = GetCurrentCommitHash()
        };

        var history = existing?.History ?? [];
        history.Add(historyEntry);

        if (history.Count > 100)
        {
            history = history.Skip(history.Count - 100).ToList();
        }

        var baselineData = new BaselineData
        {
            Subsystem = subsystem,
            Platform = results.Platform,
            LastUpdated = DateTime.UtcNow,
            UpdatedBy = updatedBy,
            ChangeReason = changeReason,
            Benchmarks = results.Results.Values.ToList(),
            History = history
        };

        var json = JsonSerializer.Serialize(baselineData, JsonOptions);

        if (File.Exists(baselinePath))
        {
            File.Copy(baselinePath, $"{baselinePath}.bak", overwrite: true);
        }

        var tempPath = $"{baselinePath}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);
        File.Move(tempPath, baselinePath, overwrite: true);

        Console.WriteLine($"Baseline saved: {baselinePath}");
    }

        public async Task<BaselineComparison> CompareToBaselineAsync(
        string subsystem,
        BenchmarkResults current)
    {
        var baseline = await LoadBaselineAsync(subsystem).ConfigureAwait(false);
        if (baseline == null)
        {
            return new BaselineComparison
            {
                Subsystem = subsystem,
                BaselineTimestamp = DateTime.MinValue,
                CurrentTimestamp = current.Timestamp,
                NewBenchmarks = current.Results.Keys.ToList()
            };
        }

        if (!current.Platform.IsCompatibleWith(baseline.Platform))
        {
            Console.WriteLine(
                $"Error: Platform mismatch between baseline and current run. Results are not comparable.");
            Console.WriteLine($"  Baseline: {baseline.Platform.Cpu} ({string.Join(", ", baseline.Platform.CpuFeatures)})");
            Console.WriteLine($"  Current:  {current.Platform.Cpu} ({string.Join(", ", current.Platform.CpuFeatures)})");

            return new BaselineComparison
            {
                Subsystem = subsystem,
                BaselineTimestamp = baseline.LastUpdated,
                CurrentTimestamp = current.Timestamp,
                PlatformMismatch = true
            };
        }

        var baselineCommit = baseline.History.Count > 0
            ? baseline.History[^1].CommitHash
            : null;
        var currentCommit = GetCurrentCommitHash();
        if (baselineCommit is not null
            && baselineCommit != "unknown"
            && currentCommit != "unknown"
            && baselineCommit != currentCommit)
        {
            Console.WriteLine(
                $"Warning: baseline for {subsystem} was recorded at commit {baselineCommit} but HEAD is {currentCommit}; baseline may be stale.");
        }

        var comparison = new BaselineComparison
        {
            Subsystem = subsystem,
            BaselineTimestamp = baseline.LastUpdated,
            CurrentTimestamp = current.Timestamp
        };

        var baselineMap = baseline.Benchmarks.ToDictionary(b => b.FullName);

        foreach (var (name, currentResult) in current.Results)
        {
            if (!baselineMap.TryGetValue(name, out var baselineResult))
            {

                comparison.NewBenchmarks.Add(name);
                continue;
            }

            var delta = CalculateDelta(baselineResult, currentResult);

            if (delta.IsSignificant && delta.PercentChange > delta.Tolerance * 100)
            {
                comparison.Regressions.Add(delta);
            }
            else if (delta.AllocationRegressed)
            {
                comparison.Regressions.Add(delta);
            }
            else if (delta.IsSignificant && delta.PercentChange < -delta.Tolerance * 100)
            {
                comparison.Improvements.Add(delta);
            }
            else
            {
                comparison.Stable.Add(delta);
            }
        }

        foreach (var baselineName in baselineMap.Keys)
        {
            if (!current.Results.ContainsKey(baselineName))
            {
                comparison.RemovedBenchmarks.Add(baselineName);
            }
        }

        return comparison;
    }

        public async Task UpdateBaselineAsync(
        string subsystem,
        BenchmarkResults current,
        string updatedBy,
        string changeReason)
    {
        await SaveBaselineAsync(subsystem, current, updatedBy, changeReason).ConfigureAwait(false);
    }

        private BenchmarkDelta CalculateDelta(BenchmarkResult baseline, BenchmarkResult current)
    {
        var meanDelta = current.Mean - baseline.Mean;

        double percentChange;
        if (baseline.Mean == 0.0)
        {
            percentChange = current.Mean == 0.0 ? 0.0 : 100.0;
        }
        else
        {
            percentChange = (meanDelta / baseline.Mean) * 100.0;
        }

        var exceedsTolerance = Math.Abs(percentChange) > baseline.Tolerance * 100.0;
        var exceedsNoiseFloor = baseline.StdDev > 0.0 && Math.Abs(meanDelta) > 3 * baseline.StdDev;
        var exceedsAbsoluteFloor = Math.Abs(meanDelta) > MINIMUM_SIGNIFICANT_DELTA_NS;
        var isSignificant = exceedsTolerance
            && exceedsAbsoluteFloor
            && exceedsNoiseFloor;

        var allocatedDelta = current.Allocated - baseline.Allocated;
        double allocatedPercentChange;
        if (baseline.Allocated == 0L)
        {
            allocatedPercentChange = current.Allocated == 0L ? 0.0 : 100.0;
        }
        else
        {
            allocatedPercentChange = ((double)allocatedDelta / baseline.Allocated) * 100.0;
        }

        var allocationRegressed = allocatedPercentChange > baseline.Tolerance * 100.0;

        return new BenchmarkDelta
        {
            BenchmarkName = baseline.FullName,
            Category = baseline.Category,
            BaselineMean = baseline.Mean,
            CurrentMean = current.Mean,
            BaselineStdDev = baseline.StdDev,
            CurrentStdDev = current.StdDev,
            PercentChange = percentChange,
            IsSignificant = isSignificant,
            Tolerance = baseline.Tolerance,
            BaselineAllocated = baseline.Allocated,
            CurrentAllocated = current.Allocated,
            AllocatedPercentChange = allocatedPercentChange,
            AllocationRegressed = allocationRegressed,
            Tags = baseline.Tags
        };
    }

        private string GetBaselinePath(string subsystem)
    {
        return Path.Combine(_baselinesRoot, subsystem, "benchmarks.json");
    }

        private string GetCurrentCommitHash()
    {
        try
        {
            var gitDir = Path.Combine(_context.SolutionRoot, ".git");
            if (!Directory.Exists(gitDir))
            {
                return "unknown";
            }

            var headPath = Path.Combine(gitDir, "HEAD");
            if (!File.Exists(headPath))
            {
                return "unknown";
            }

            var head = File.ReadAllText(headPath).Trim();

            if (head.StartsWith("ref: "))
            {
                var refPath = head.Substring(5);
                var refFile = Path.Combine(gitDir, refPath);
                if (File.Exists(refFile))
                {
                    var hash = File.ReadAllText(refFile).Trim();
                    return hash.Length > 8 ? hash.Substring(0, 8) : hash;
                }
            }
            else
            {

                return head.Length > 8 ? head.Substring(0, 8) : head;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (_context.Verbose)
            {
                Console.WriteLine($"Warning: could not read git commit hash: {ex.Message}");
            }
        }

        return "unknown";
    }

        public void DisplayComparison(BaselineComparison comparison)
    {
        if (comparison.BaselineTimestamp == DateTime.MinValue)
        {
            Console.WriteLine($"\n⚠ No baseline found for {comparison.Subsystem}");
            Console.WriteLine($"   Run 'smash baseline record {comparison.Subsystem}' to create baseline");
            return;
        }

        Console.WriteLine($"\nBaseline Comparison (.baselines/{comparison.Subsystem}/benchmarks.json, {comparison.BaselineTimestamp:yyyy-MM-dd}):");

        if (comparison.Stable.Count > 0)
        {
            Console.WriteLine($"  ✓ {comparison.Stable.Count} benchmarks stable");
        }

        if (comparison.Improvements.Count > 0)
        {
            Console.WriteLine($"  ✓ {comparison.Improvements.Count} improvements:");
            foreach (var delta in comparison.Improvements.Take(5))
            {
                Console.WriteLine($"    {delta.Summary}");
            }
            if (comparison.Improvements.Count > 5)
            {
                Console.WriteLine($"    ... and {comparison.Improvements.Count - 5} more");
            }
        }

        if (comparison.Regressions.Count > 0)
        {
            Console.WriteLine($"  ⚠ {comparison.Regressions.Count} regressions:");
            foreach (var delta in comparison.Regressions)
            {
                Console.WriteLine($"    {delta.Summary}");
                if (delta.Tags.Contains("critical") || delta.Tags.Contains("hot-path"))
                {
                    Console.WriteLine($"      [CRITICAL] This is a hot-path benchmark");
                }
            }

            Console.WriteLine();
            Console.WriteLine("  Possible causes:");
            Console.WriteLine("    - Code change introduced overhead");
            Console.WriteLine("    - Hardware throttling (check thermals)");
            Console.WriteLine("    - Background processes interfering");
            Console.WriteLine();
            Console.WriteLine($"  To update baseline (if intentional): smash baseline update {comparison.Subsystem} --approve");
        }

        if (comparison.NewBenchmarks.Count > 0)
        {
            Console.WriteLine($"  ℹ {comparison.NewBenchmarks.Count} new benchmarks (not in baseline)");
        }
        if (comparison.RemovedBenchmarks.Count > 0)
        {
            Console.WriteLine($"  ℹ {comparison.RemovedBenchmarks.Count} benchmarks removed from suite");
        }
    }
}

public sealed class BaselineData
{
        public required string Subsystem { get; init; }

        public required PlatformInfo Platform { get; init; }

        public required DateTime LastUpdated { get; init; }

        public required string UpdatedBy { get; init; }

        public required string ChangeReason { get; init; }

        public required List<BenchmarkResult> Benchmarks { get; init; }

        public List<BaselineHistoryEntry> History { get; init; } = [];
}

public sealed class BaselineHistoryEntry
{
        public required DateTime Timestamp { get; init; }

        public required string UpdatedBy { get; init; }

        public required string ChangeReason { get; init; }

        public required List<string> BenchmarksChanged { get; init; }

        public required string CommitHash { get; init; }
}
