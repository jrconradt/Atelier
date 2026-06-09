using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Atelier.Framework.Performance;

var summary = BenchmarkRunner.Run<PerformanceBench>();
BenchmarkResultEmitter.Emit(summary);

[MemoryDiagnoser]
public class PerformanceBench
{
    private readonly PerformanceProfiler _profiler = new();

    private static PerformanceMetric NewMetric()
    {
        return new PerformanceMetric
        {
            MetricId = "m",
            Component = "checkout",
            Operation = "submit",
            Type = MetricType.Latency,
            Value = 12.5,
            Unit = "ms"
        };
    }

    [GlobalSetup]
    public async Task Setup()
    {
        for (int i = 0; i < 64; i++)
        {
            await _profiler.RecordMetricAsync(NewMetric()).ConfigureAwait(false);
        }
    }

    [Benchmark]
    [BenchmarkCategory("Metric")]
    public MetricType ConstructMetric()
    {
        var metric = NewMetric();
        return metric.Type;
    }

    [Benchmark]
    [BenchmarkCategory("Metric")]
    public async Task<bool> RecordMetric()
    {
        var recorded = await _profiler.RecordMetricAsync(NewMetric()).ConfigureAwait(false);
        return recorded.IsSuccess;
    }

    [Benchmark]
    [BenchmarkCategory("Aggregation")]
    public async Task<bool> GetComponentMetrics()
    {
        await _profiler.RecordMetricAsync(NewMetric()).ConfigureAwait(false);
        var metrics = await _profiler.GetComponentMetricsAsync("checkout").ConfigureAwait(false);
        return metrics.IsSuccess;
    }

    [Benchmark]
    [BenchmarkCategory("Scope")]
    public bool StartOperationScope()
    {
        using var scope = _profiler.StartOperation("checkout",
                                                   "submit");
        return scope is not null;
    }
}

public static class BenchmarkResultEmitter
{
    public static void Emit(Summary summary)
    {
        foreach (var report in summary.Reports)
        {
            var descriptor = report.BenchmarkCase.Descriptor;
            var statistics = report.ResultStatistics;
            var allocated = report.GcStats.GetBytesAllocatedPerOperation(report.BenchmarkCase) ?? 0L;

            var result = new
            {
                Category = descriptor.Categories.Length > 0 ? descriptor.Categories[0] : string.Empty,
                ClassName = descriptor.Type.Name,
                MethodName = descriptor.WorkloadMethod.Name,
                Mean = statistics?.Mean ?? 0.0,
                StdDev = statistics?.StandardDeviation ?? 0.0,
                Allocated = allocated,
                Unit = "ns",
                Tolerance = 0.10
            };

            Console.WriteLine(JsonSerializer.Serialize(result));
        }
    }
}
