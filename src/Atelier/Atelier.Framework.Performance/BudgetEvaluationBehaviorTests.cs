using Atelier.Framework.Testing;

namespace Atelier.Framework.Performance;

public static class BudgetEvaluationBehaviorTests
{
    private static PerformanceBudget Budget()
    {
        return new PerformanceBudget
        {
            Component = "checkout",
            MaxP95LatencyMs = 100,
            MaxP99LatencyMs = 200,
            MaxErrorRate = 0.01
        };
    }

    [GeneratedTest("Performance/Budget-Within-Limits-Has-No-Breaches", "global::Atelier.Framework.Performance.PerformanceBudget")]
    public static void WithinBudgetProducesNoBreaches()
    {
        var metrics = new ComponentMetrics
        {
            ComponentName = "checkout",
            P95LatencyMs = 50,
            P99LatencyMs = 150,
            ErrorRate = 0.005
        };

        var breaches = BudgetEvaluation.Evaluate(Budget(), metrics);
        if (breaches.Count != 0)
        {
            throw new InvalidOperationException($"expected no breaches when metrics are within budget, got {breaches.Count}");
        }
    }

    [GeneratedTest("Performance/Budget-P95-Overrun-Raises-Warning", "global::Atelier.Framework.Performance.PerformanceBudget")]
    public static void P95OverrunRaisesWarningBreach()
    {
        var metrics = new ComponentMetrics
        {
            ComponentName = "checkout",
            P95LatencyMs = 150,
            P99LatencyMs = 150,
            ErrorRate = 0
        };

        var breaches = BudgetEvaluation.Evaluate(Budget(), metrics);
        if (breaches.Count != 1)
        {
            throw new InvalidOperationException($"expected exactly one breach for p95 overrun, got {breaches.Count}");
        }
        var breach = breaches[0];
        if (breach.Metric != "p95_latency_ms")
        {
            throw new InvalidOperationException($"expected p95_latency_ms breach, got '{breach.Metric}'");
        }
        if (breach.Severity != AlertSeverity.Warning)
        {
            throw new InvalidOperationException($"expected Warning severity for p95, got {breach.Severity}");
        }
        if (breach.Threshold != 100
            || breach.ActualValue != 150)
        {
            throw new InvalidOperationException($"breach carried wrong threshold/actual: {breach.Threshold}/{breach.ActualValue}");
        }
    }

    [GeneratedTest("Performance/Budget-P99-Overrun-Raises-Critical", "global::Atelier.Framework.Performance.PerformanceBudget")]
    public static void P99OverrunRaisesCriticalBreach()
    {
        var metrics = new ComponentMetrics
        {
            ComponentName = "checkout",
            P95LatencyMs = 50,
            P99LatencyMs = 250,
            ErrorRate = 0
        };

        var breaches = BudgetEvaluation.Evaluate(Budget(), metrics);
        if (breaches.Count != 1)
        {
            throw new InvalidOperationException($"expected exactly one breach for p99 overrun, got {breaches.Count}");
        }
        if (breaches[0].Metric != "p99_latency_ms"
            || breaches[0].Severity != AlertSeverity.Critical)
        {
            throw new InvalidOperationException($"expected critical p99_latency_ms breach, got '{breaches[0].Metric}' {breaches[0].Severity}");
        }
    }

    [GeneratedTest("Performance/Budget-Error-Rate-Overrun-Raises-Critical", "global::Atelier.Framework.Performance.PerformanceBudget")]
    public static void ErrorRateOverrunRaisesCriticalBreach()
    {
        var metrics = new ComponentMetrics
        {
            ComponentName = "checkout",
            P95LatencyMs = 50,
            P99LatencyMs = 150,
            ErrorRate = 0.05
        };

        var breaches = BudgetEvaluation.Evaluate(Budget(), metrics);
        if (breaches.Count != 1)
        {
            throw new InvalidOperationException($"expected exactly one breach for error-rate overrun, got {breaches.Count}");
        }
        if (breaches[0].Metric != "error_rate"
            || breaches[0].Severity != AlertSeverity.Critical)
        {
            throw new InvalidOperationException($"expected critical error_rate breach, got '{breaches[0].Metric}' {breaches[0].Severity}");
        }
    }

    [GeneratedTest("Performance/Budget-Zero-Threshold-Is-Disabled", "global::Atelier.Framework.Performance.PerformanceBudget")]
    public static void ZeroThresholdDisablesThatDimension()
    {
        var budget = new PerformanceBudget
        {
            Component = "checkout",
            MaxP95LatencyMs = 0,
            MaxP99LatencyMs = 0,
            MaxErrorRate = 0
        };
        var metrics = new ComponentMetrics
        {
            ComponentName = "checkout",
            P95LatencyMs = 9999,
            P99LatencyMs = 9999,
            ErrorRate = 1
        };

        var breaches = BudgetEvaluation.Evaluate(budget, metrics);
        if (breaches.Count != 0)
        {
            throw new InvalidOperationException($"expected zero thresholds to disable evaluation, got {breaches.Count} breaches");
        }
    }

    [GeneratedTest("Performance/Budget-All-Dimensions-Overrun-Raises-Three-Breaches", "global::Atelier.Framework.Performance.PerformanceBudget")]
    public static void AllDimensionsOverrunRaisesEveryBreach()
    {
        var metrics = new ComponentMetrics
        {
            ComponentName = "checkout",
            P95LatencyMs = 150,
            P99LatencyMs = 250,
            ErrorRate = 0.5
        };

        var breaches = BudgetEvaluation.Evaluate(Budget(), metrics);
        if (breaches.Count != 3)
        {
            throw new InvalidOperationException($"expected three breaches when every dimension overruns, got {breaches.Count}");
        }
        var metricNames = breaches.Select(b => b.Metric).OrderBy(m => m).ToList();
        if (metricNames[0] != "error_rate"
            || metricNames[1] != "p95_latency_ms"
            || metricNames[2] != "p99_latency_ms")
        {
            throw new InvalidOperationException($"expected error_rate/p95/p99 breaches, got {string.Join(",", metricNames)}");
        }
    }

    [GeneratedTest("Performance/Budget-Exact-Threshold-Is-Not-A-Breach", "global::Atelier.Framework.Performance.PerformanceBudget")]
    public static void ValueEqualToThresholdDoesNotBreach()
    {
        var metrics = new ComponentMetrics
        {
            ComponentName = "checkout",
            P95LatencyMs = 100,
            P99LatencyMs = 200,
            ErrorRate = 0.01
        };

        var breaches = BudgetEvaluation.Evaluate(Budget(), metrics);
        if (breaches.Count != 0)
        {
            throw new InvalidOperationException($"expected no breaches when metrics equal thresholds, got {breaches.Count}");
        }
    }
}

public static class PercentileCalculationBehaviorTests
{
    [GeneratedTest("Performance/Percentile-Empty-Series-Is-Zero", "global::Atelier.Framework.Performance.PerformanceMetric")]
    public static void EmptySeriesYieldsZero()
    {
        var value = MetricCalculations.GetPercentile(new List<double>(), 0.95);
        if (value != 0)
        {
            throw new InvalidOperationException($"expected 0 for an empty series, got {value}");
        }
    }

    [GeneratedTest("Performance/Percentile-P50-And-P95-Pick-Expected-Samples", "global::Atelier.Framework.Performance.PerformanceMetric")]
    public static void PercentilesSelectExpectedSamples()
    {
        var sorted = new List<double> { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 };

        var p50 = MetricCalculations.GetPercentile(sorted, 0.50);
        if (p50 != 50)
        {
            throw new InvalidOperationException($"expected p50 of 50, got {p50}");
        }

        var p95 = MetricCalculations.GetPercentile(sorted, 0.95);
        if (p95 != 100)
        {
            throw new InvalidOperationException($"expected p95 of 100, got {p95}");
        }

        var p99 = MetricCalculations.GetPercentile(sorted, 0.99);
        if (p99 != 100)
        {
            throw new InvalidOperationException($"expected p99 of 100, got {p99}");
        }
    }

    [GeneratedTest("Performance/Component-Metrics-Computed-From-Mixed-Series", "global::Atelier.Framework.Performance.PerformanceMetric")]
    public static void ComponentMetricsComputedFromMixedSeries()
    {
        var metrics = new List<PerformanceMetric>
        {
            new PerformanceMetric { Component = "api", Type = MetricType.Latency, Value = 10 },
            new PerformanceMetric { Component = "api", Type = MetricType.Latency, Value = 20 },
            new PerformanceMetric { Component = "api", Type = MetricType.Latency, Value = 30 },
            new PerformanceMetric { Component = "api", Type = MetricType.Latency, Value = 40 },
            new PerformanceMetric { Component = "api", Type = MetricType.ErrorRate, Value = 0.2 },
            new PerformanceMetric { Component = "api", Type = MetricType.Memory, Value = 4096 }
        };

        var result = MetricCalculations.CalculateComponentMetrics("api", metrics);
        if (result.ComponentName != "api")
        {
            throw new InvalidOperationException($"expected component 'api', got '{result.ComponentName}'");
        }
        if (result.TotalOperations != 4)
        {
            throw new InvalidOperationException($"expected 4 latency operations, got {result.TotalOperations}");
        }
        if (result.AverageLatencyMs != 25)
        {
            throw new InvalidOperationException($"expected average latency 25, got {result.AverageLatencyMs}");
        }
        if (result.ErrorCount != 1
            || result.ErrorRate != 0.2)
        {
            throw new InvalidOperationException($"expected 1 error at rate 0.2, got {result.ErrorCount} at {result.ErrorRate}");
        }
        if (result.MemoryAllocatedBytes != 4096)
        {
            throw new InvalidOperationException($"expected last memory sample 4096, got {result.MemoryAllocatedBytes}");
        }
    }
}

public static class MetricStoreBehaviorTests
{
    private static PerformanceMetric Sample(
        string component,
        string operation,
        DateTime timestamp)
    {
        return new PerformanceMetric
        {
            Component = component,
            Operation = operation,
            Type = MetricType.Latency,
            Value = 1,
            Timestamp = timestamp
        };
    }

    [GeneratedTest("Performance/MetricStore-Records-And-Snapshots-By-Key", "global::Atelier.Framework.Performance.PerformanceMetric")]
    public static void RecordedMetricIsReturnedBySnapshotKey()
    {
        var store = new MetricStore(TimeSpan.FromHours(1));
        var now = DateTime.UtcNow;
        store.Record(Sample("api", "read", now));
        store.Record(Sample("api", "read", now));

        var snapshot = store.SnapshotKey("api:read", now.AddMinutes(-1));
        if (snapshot.Count != 2)
        {
            throw new InvalidOperationException($"expected 2 recorded metrics for api:read, got {snapshot.Count}");
        }
    }

    [GeneratedTest("Performance/MetricStore-Drops-Samples-Older-Than-Retention", "global::Atelier.Framework.Performance.PerformanceMetric")]
    public static void SamplesOlderThanRetentionAreDropped()
    {
        var store = new MetricStore(TimeSpan.FromMinutes(5));
        var now = DateTime.UtcNow;
        store.Record(Sample("api", "read", now.AddMinutes(-10)));
        store.Record(Sample("api", "read", now));

        var snapshot = store.SnapshotKey("api:read", now.AddHours(-1));
        if (snapshot.Count != 1)
        {
            throw new InvalidOperationException($"expected retention to evict the stale sample, got {snapshot.Count}");
        }
        if (snapshot[0].Timestamp != now)
        {
            throw new InvalidOperationException("expected the surviving sample to be the recent one");
        }
    }

    [GeneratedTest("Performance/MetricStore-Snapshot-By-Prefix-Filters-Components", "global::Atelier.Framework.Performance.PerformanceMetric")]
    public static void SnapshotByPrefixOnlyReturnsMatchingKeys()
    {
        var store = new MetricStore(TimeSpan.FromHours(1));
        var now = DateTime.UtcNow;
        store.Record(Sample("api", "read", now));
        store.Record(Sample("worker", "drain", now));

        var apiOnly = store.SnapshotByPrefix("api:", now.AddMinutes(-1));
        if (apiOnly.Count != 1)
        {
            throw new InvalidOperationException($"expected only the api sample under prefix 'api:', got {apiOnly.Count}");
        }
        if (apiOnly[0].Component != "api")
        {
            throw new InvalidOperationException($"expected api component under prefix, got '{apiOnly[0].Component}'");
        }
    }

    [GeneratedTest("Performance/MetricStore-Prune-Removes-Emptied-Keys", "global::Atelier.Framework.Performance.PerformanceMetric")]
    public static void PruneRemovesKeysWhoseSamplesAllExpired()
    {
        var store = new MetricStore(TimeSpan.FromMinutes(5));
        var now = DateTime.UtcNow;
        store.Record(Sample("api", "read", now.AddMinutes(-30)));

        store.Prune();

        var snapshot = store.SnapshotByComponent(now.AddHours(-1));
        if (snapshot.ContainsKey("api"))
        {
            throw new InvalidOperationException("expected prune to remove the fully-expired api key");
        }
    }
}

public static class BaselineRegistryBehaviorTests
{
    private static PerformanceBaseline Baseline(DateTime createdAt)
    {
        return new PerformanceBaseline
        {
            Component = "api",
            Operation = "read",
            BaselineValue = 42,
            CreatedAt = createdAt
        };
    }

    [GeneratedTest("Performance/BaselineRegistry-Set-Then-Get-Returns-Value", "global::Atelier.Framework.Performance.PerformanceBaseline")]
    public static void SetThenGetReturnsStoredBaseline()
    {
        var registry = new BaselineRegistry(TimeSpan.FromHours(1));
        registry.Set("api:read", Baseline(DateTime.UtcNow));

        if (!registry.TryGet("api:read", out var found))
        {
            throw new InvalidOperationException("expected to retrieve a baseline that was just set");
        }
        if (found.BaselineValue != 42)
        {
            throw new InvalidOperationException($"expected baseline value 42, got {found.BaselineValue}");
        }
    }

    [GeneratedTest("Performance/BaselineRegistry-Missing-Key-Returns-False", "global::Atelier.Framework.Performance.PerformanceBaseline")]
    public static void MissingKeyReportsNotFound()
    {
        var registry = new BaselineRegistry(TimeSpan.FromHours(1));
        if (registry.TryGet("absent:key", out _))
        {
            throw new InvalidOperationException("expected TryGet to report missing for an unknown key");
        }
    }

    [GeneratedTest("Performance/BaselineRegistry-Evict-Drops-Expired-Baselines", "global::Atelier.Framework.Performance.PerformanceBaseline")]
    public static void EvictDropsExpiredBaselines()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var now = clock.GetUtcNow().UtcDateTime;
        var registry = new BaselineRegistry(TimeSpan.FromMinutes(5), clock);
        registry.Set("stale", Baseline(now.AddMinutes(-30)));
        registry.Set("fresh", Baseline(now));

        registry.Evict();

        if (registry.TryGet("stale", out _))
        {
            throw new InvalidOperationException("expected eviction to drop the expired baseline");
        }
        if (!registry.TryGet("fresh", out _))
        {
            throw new InvalidOperationException("expected eviction to keep the in-window baseline");
        }
    }
}

public static class AlertSinkBehaviorTests
{
    private static PerformanceAlert Alert(
        AlertSeverity severity,
        DateTime timestamp)
    {
        return new PerformanceAlert
        {
            Severity = severity,
            Timestamp = timestamp
        };
    }

    [GeneratedTest("Performance/AlertSink-Caps-Active-Alerts", "global::Atelier.Framework.Performance.PerformanceAlert")]
    public static void ActiveAlertsAreCappedAtMax()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var sink = new AlertSink(TimeSpan.FromHours(1), 2, clock);
        var now = clock.GetUtcNow().UtcDateTime;
        sink.Add(Alert(AlertSeverity.Info, now));
        sink.Add(Alert(AlertSeverity.Warning, now));
        sink.Add(Alert(AlertSeverity.Critical, now));

        var snapshot = sink.Snapshot();
        if (snapshot.Count != 2)
        {
            throw new InvalidOperationException($"expected the active-alert cap of 2 to hold, got {snapshot.Count}");
        }
    }

    [GeneratedTest("Performance/AlertSink-Severity-Filter-Orders-By-Severity", "global::Atelier.Framework.Performance.PerformanceAlert")]
    public static void SeverityFilteredSnapshotOrdersBySeverity()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var sink = new AlertSink(TimeSpan.FromHours(1), 10, clock);
        var now = clock.GetUtcNow().UtcDateTime;
        sink.Add(Alert(AlertSeverity.Info, now));
        sink.Add(Alert(AlertSeverity.Critical, now));
        sink.Add(Alert(AlertSeverity.Warning, now));

        var snapshot = sink.Snapshot(AlertSeverity.Warning);
        if (snapshot.Count != 2)
        {
            throw new InvalidOperationException($"expected only Warning-and-above alerts, got {snapshot.Count}");
        }
        if (snapshot[0].Severity != AlertSeverity.Critical
            || snapshot[1].Severity != AlertSeverity.Warning)
        {
            throw new InvalidOperationException($"expected severity-descending order, got {snapshot[0].Severity} then {snapshot[1].Severity}");
        }
    }
}

internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _instant;

    public FixedTimeProvider(DateTimeOffset instant)
    {
        _instant = instant;
    }

    public override DateTimeOffset GetUtcNow()
    {
        return _instant;
    }
}
