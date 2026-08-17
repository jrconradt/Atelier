using System.Collections.Concurrent;
using Atelier.Framework.Context;
using Atelier.Framework.Attributes;
using Atelier.Framework.Infrastructure.Operation;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;

namespace Atelier.Framework.Performance;

public partial class MetricsAggregator : IAtelier, IMetricsAggregator
{

    private static readonly TimeSpan AggregateRetentionWindow = TimeSpan.FromHours(1);
    private const int MAX_AGGREGATES = 1000;
    private readonly ConcurrentDictionary<string, AggregatedMetrics> _aggregates = new();

    [Operation("AggregateMetrics")]
    public Task<Outcome<AggregatedMetrics>> AggregateMetricsAsync(
        List<PerformanceMetric> metrics,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Outcome<AggregatedMetrics>.Failure());
        }

        if (metrics is null)
        {
            Observe(LogLevel.Warning, values: [("Reason", $"{nameof(metrics)} cannot be null")]);
            return Task.FromResult(Outcome<AggregatedMetrics>.Failure());
        }

        Observe(values: [("Count", metrics.Count), ("Window", window)]);

        try
        {
            var windowStart = DateTime.UtcNow - window;
            var relevantMetrics = metrics.Where(m => m.Timestamp >= windowStart).ToList();

            var componentAggregates = new Dictionary<string, ComponentAggregate>();
            var componentGroups = relevantMetrics.GroupBy(m => m.Component);

            foreach (var group in componentGroups)
            {
                var componentAggregate = AggregateComponent(group.Key, group.ToList());
                componentAggregates[group.Key] = componentAggregate;
            }

            var systemAggregate = AggregateSystem(componentAggregates.Values.ToList());

            var aggregate = new AggregatedMetrics
            {
                AggregateId = Guid.NewGuid().ToString("N"),
                WindowStart = windowStart,
                WindowEnd = DateTime.UtcNow,
                TotalMetrics = relevantMetrics.Count,
                ComponentAggregates = componentAggregates,
                SystemAggregate = systemAggregate
            };

            _aggregates[aggregate.AggregateId] = aggregate;
            EvictAggregates();

            return Task.FromResult(Outcome<AggregatedMetrics>.Success(aggregate));
        }
        catch (Exception ex)
        {
            Observe(LogLevel.Error, ex, values: [("Reason", "Failed to aggregate metrics")]);
            return Task.FromResult(Outcome<AggregatedMetrics>.Failure());
        }
    }

    [Operation("GetSystemWideTrends")]
    public Task<Outcome<SystemTrends>> GetSystemWideTrendsAsync(
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Outcome<SystemTrends>.Failure());
        }

        Observe(values: [("Window", window)]);

        try
        {
            var windowStart = DateTime.UtcNow - window;

            var relevantAggregates = _aggregates.Values
                .Where(a => a.WindowStart >= windowStart)
                .OrderBy(a => a.WindowStart)
                .ToList();

            if (!relevantAggregates.Any())
            {
                Observe(LogLevel.Information, values: [("Reason", "No aggregates found in window"), ("Window", window)]);
                return Task.FromResult(Outcome<SystemTrends>.Failure());
            }

            var trends = new SystemTrends
            {
                TrendsId = Guid.NewGuid().ToString("N"),
                WindowStart = windowStart,
                WindowEnd = DateTime.UtcNow,
                LatencyTrend = CalculateTrend(relevantAggregates.Select(a => a.SystemAggregate.AverageLatencyMs).ToList()),
                ThroughputTrend = CalculateTrend(relevantAggregates.Select(a => (double)a.SystemAggregate.TotalOperations).ToList()),
                ErrorRateTrend = CalculateTrend(relevantAggregates.Select(a => a.SystemAggregate.ErrorRate).ToList()),
                MemoryTrend = CalculateTrend(relevantAggregates.Select(a => (double)a.SystemAggregate.TotalMemoryBytes).ToList()),
                ComponentTrends = new Dictionary<string, TrendDirection>()
            };

            var allComponents = relevantAggregates.SelectMany(a => a.ComponentAggregates.Keys).Distinct();

            foreach (var component in allComponents)
            {
                var componentLatencies = relevantAggregates
                    .Where(a => a.ComponentAggregates.ContainsKey(component))
                    .Select(a => a.ComponentAggregates[component].AverageLatencyMs)
                    .ToList();

                trends.ComponentTrends[component] = CalculateTrend(componentLatencies);
            }

            return Task.FromResult(Outcome<SystemTrends>.Success(trends));
        }
        catch (Exception ex)
        {
            Observe(LogLevel.Error, ex, values: [("Reason", "Failed to calculate trends")]);
            return Task.FromResult(Outcome<SystemTrends>.Failure());
        }
    }

    [Operation("CompareAggregates")]
    public Task<Outcome<AggregateComparison>> CompareAggregatesAsync(
        string baselineAggregateId,
        string currentAggregateId,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Outcome<AggregateComparison>.Failure());
        }

        if (baselineAggregateId is null)
        {
            Observe(LogLevel.Warning, values: [("Reason", $"{nameof(baselineAggregateId)} cannot be null")]);
            return Task.FromResult(Outcome<AggregateComparison>.Failure());
        }

        if (currentAggregateId is null)
        {
            Observe(LogLevel.Warning, values: [("Reason", $"{nameof(currentAggregateId)} cannot be null")]);
            return Task.FromResult(Outcome<AggregateComparison>.Failure());
        }


        try
        {
            if (!_aggregates.TryGetValue(baselineAggregateId, out var baseline))
            {
                Observe(LogLevel.Warning, values: [("Reason", "Baseline aggregate not found"), ("BaselineAggregateId", baselineAggregateId)]);
                return Task.FromResult(Outcome<AggregateComparison>.Failure());
            }

            if (!_aggregates.TryGetValue(currentAggregateId, out var current))
            {
                Observe(LogLevel.Warning, values: [("Reason", "Current aggregate not found"), ("CurrentAggregateId", currentAggregateId)]);
                return Task.FromResult(Outcome<AggregateComparison>.Failure());
            }

            var comparison = new AggregateComparison
            {
                ComparisonId = Guid.NewGuid().ToString("N"),
                BaselineAggregateId = baselineAggregateId,
                CurrentAggregateId = currentAggregateId,
                LatencyChange = CalculatePercentChange(baseline.SystemAggregate.AverageLatencyMs, current.SystemAggregate.AverageLatencyMs),
                ThroughputChange = CalculatePercentChange(baseline.SystemAggregate.TotalOperations, current.SystemAggregate.TotalOperations),
                ErrorRateChange = CalculatePercentChange(baseline.SystemAggregate.ErrorRate, current.SystemAggregate.ErrorRate),
                MemoryChange = CalculatePercentChange(baseline.SystemAggregate.TotalMemoryBytes, current.SystemAggregate.TotalMemoryBytes),
                ComponentComparisons = new Dictionary<string, ComponentComparison>()
            };

            var allComponents = baseline.ComponentAggregates.Keys.Union(current.ComponentAggregates.Keys).ToList();

            foreach (var component in allComponents)
            {
                var hasBaseline = baseline.ComponentAggregates.TryGetValue(component, out var baselineComp);
                var hasCurrent = current.ComponentAggregates.TryGetValue(component, out var currentComp);

                if (hasBaseline && hasCurrent)
                {
                    comparison.ComponentComparisons[component] = new ComponentComparison
                    {
                        ComponentName = component,
                        LatencyChange = CalculatePercentChange(baselineComp!.AverageLatencyMs, currentComp!.AverageLatencyMs),
                        ThroughputChange = CalculatePercentChange(baselineComp.TotalOperations, currentComp.TotalOperations),
                        ErrorRateChange = CalculatePercentChange(baselineComp.ErrorRate, currentComp.ErrorRate)
                    };
                }
            }

            return Task.FromResult(Outcome<AggregateComparison>.Success(comparison));
        }
        catch (Exception ex)
        {
            Observe(LogLevel.Error, ex, values: [("Reason", "Failed to compare")]);
            return Task.FromResult(Outcome<AggregateComparison>.Failure());
        }
    }

    private ComponentAggregate AggregateComponent(string componentName, List<PerformanceMetric> metrics)
    {
        var latencyMetrics = metrics.Where(m => m.Type == MetricType.Latency).Select(m => m.Value).ToList();
        var memoryMetrics = metrics.Where(m => m.Type == MetricType.Memory).Select(m => m.Value).ToList();
        var errorMetrics = metrics.Where(m => m.Type == MetricType.ErrorRate).ToList();

        return new ComponentAggregate
        {
            ComponentName = componentName,
            AverageLatencyMs = latencyMetrics.Any() ? latencyMetrics.Average() : 0,
            MinLatencyMs = latencyMetrics.Any() ? latencyMetrics.Min() : 0,
            MaxLatencyMs = latencyMetrics.Any() ? latencyMetrics.Max() : 0,
            TotalOperations = latencyMetrics.Count,
            ErrorCount = errorMetrics.Count,
            ErrorRate = latencyMetrics.Any() ? (double)errorMetrics.Count / latencyMetrics.Count : 0,
            TotalMemoryBytes = memoryMetrics.Any() ? (long)memoryMetrics.Sum() : 0
        };
    }

    private SystemAggregate AggregateSystem(List<ComponentAggregate> components)
    {
        var totalOps = components.Sum(c => c.TotalOperations);
        var totalErrors = components.Sum(c => c.ErrorCount);

        return new SystemAggregate
        {
            AverageLatencyMs = components.Any() ? components.Average(c => c.AverageLatencyMs) : 0,
            TotalOperations = totalOps,
            TotalErrors = totalErrors,
            ErrorRate = totalOps > 0 ? (double)totalErrors / totalOps : 0,
            TotalMemoryBytes = components.Sum(c => c.TotalMemoryBytes),
            ComponentCount = components.Count
        };
    }

    private TrendDirection CalculateTrend(List<double> values)
    {
        if (values.Count < 2)
        {
            return TrendDirection.Stable;
        }

        var firstHalf = values.Take(values.Count / 2).Average();
        var secondHalf = values.Skip(values.Count / 2).Average();

        if (firstHalf == 0)
        {
            if (secondHalf == 0)
            {
                return TrendDirection.Stable;
            }

            return secondHalf > 0 ? TrendDirection.Increasing : TrendDirection.Decreasing;
        }

        var changePercent = Math.Abs((secondHalf - firstHalf) / firstHalf * 100);

        if (changePercent < 5)
        {
            return TrendDirection.Stable;
        }

        return secondHalf > firstHalf ? TrendDirection.Increasing : TrendDirection.Decreasing;
    }

    private double CalculatePercentChange(double baseline, double current)
    {
        if (baseline == 0)
        {
            return 0;
        }

        return ((current - baseline) / baseline) * 100;
    }

    private void EvictAggregates()
    {
        var cutoff = DateTime.UtcNow - AggregateRetentionWindow;
        var expiredKeys = _aggregates
            .Where(kvp => kvp.Value.WindowEnd < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _aggregates.TryRemove(key, out _);
        }

        var overflow = _aggregates.Count - MAX_AGGREGATES;
        if (overflow > 0)
        {
            var oldestKeys = _aggregates
                .OrderBy(kvp => kvp.Value.WindowEnd)
                .Take(overflow)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in oldestKeys)
            {
                _aggregates.TryRemove(key, out _);
            }
        }
    }
}

public interface IMetricsAggregator
{
    public Task<Outcome<AggregatedMetrics>> AggregateMetricsAsync(
        List<PerformanceMetric> metrics,
        TimeSpan window,
        CancellationToken cancellationToken = default);

    public Task<Outcome<SystemTrends>> GetSystemWideTrendsAsync(
        TimeSpan window,
        CancellationToken cancellationToken = default);

    public Task<Outcome<AggregateComparison>> CompareAggregatesAsync(
        string baselineAggregateId,
        string currentAggregateId,
        CancellationToken cancellationToken = default);
}

[ContractAttribute("AggregatedMetrics", Version = "1.0")]
public class AggregatedMetrics
{
    public required string AggregateId { get; set; }
    public DateTime WindowStart { get; set; }
    public DateTime WindowEnd { get; set; }
    public int TotalMetrics { get; set; }
    public required Dictionary<string, ComponentAggregate> ComponentAggregates { get; set; }
    public required SystemAggregate SystemAggregate { get; set; }
}

[ContractAttribute("ComponentAggregate", Version = "1.0")]
public class ComponentAggregate
{
    public required string ComponentName { get; set; }
    public double AverageLatencyMs { get; set; }
    public double MinLatencyMs { get; set; }
    public double MaxLatencyMs { get; set; }
    public long TotalOperations { get; set; }
    public long ErrorCount { get; set; }
    public double ErrorRate { get; set; }
    public long TotalMemoryBytes { get; set; }
}

[ContractAttribute("SystemAggregate", Version = "1.0")]
public class SystemAggregate
{
    public double AverageLatencyMs { get; set; }
    public long TotalOperations { get; set; }
    public long TotalErrors { get; set; }
    public double ErrorRate { get; set; }
    public long TotalMemoryBytes { get; set; }
    public int ComponentCount { get; set; }
}

[ContractAttribute("SystemTrends", Version = "1.0")]
public class SystemTrends
{
    public required string TrendsId { get; set; }
    public DateTime WindowStart { get; set; }
    public DateTime WindowEnd { get; set; }
    public TrendDirection LatencyTrend { get; set; }
    public TrendDirection ThroughputTrend { get; set; }
    public TrendDirection ErrorRateTrend { get; set; }
    public TrendDirection MemoryTrend { get; set; }
    public required Dictionary<string, TrendDirection> ComponentTrends { get; set; }
}

public enum TrendDirection
{
    Decreasing,
    Stable,
    Increasing
}

[ContractAttribute("AggregateComparison", Version = "1.0")]
public class AggregateComparison
{
    public required string ComparisonId { get; set; }
    public required string BaselineAggregateId { get; set; }
    public required string CurrentAggregateId { get; set; }
    public double LatencyChange { get; set; }
    public double ThroughputChange { get; set; }
    public double ErrorRateChange { get; set; }
    public double MemoryChange { get; set; }
    public required Dictionary<string, ComponentComparison> ComponentComparisons { get; set; }
}

[ContractAttribute("ComponentComparison", Version = "1.0")]
public class ComponentComparison
{
    public required string ComponentName { get; set; }
    public double LatencyChange { get; set; }
    public double ThroughputChange { get; set; }
    public double ErrorRateChange { get; set; }
}
