using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Atelier.Framework.Outcomes;

var summary = BenchmarkRunner.Run<OutcomesBench>();
BenchmarkResultEmitter.Emit(summary);

[MemoryDiagnoser]
public class OutcomesBench
{
    [Benchmark]
    [BenchmarkCategory("Outcome")]
    public bool SuccessAllocation()
    {
        var outcome = Outcome<int>.Success(7);
        return outcome.IsSuccess;
    }

    [Benchmark]
    [BenchmarkCategory("Outcome")]
    public bool FailureAllocation()
    {
        var outcome = Outcome<int>.Failure();
        return outcome.IsSuccess;
    }

    [Benchmark]
    [BenchmarkCategory("Outcome")]
    public int BindChain()
    {
        var outcome = Outcome<int>.Success(1)
            .Bind(x => Outcome<int>.Success(x + 1))
            .Bind(x => Outcome<int>.Success(x * 2))
            .Bind(x => Outcome<int>.Success(x - 1));
        return outcome.Data;
    }

    [Benchmark]
    [BenchmarkCategory("Outcome")]
    public string? MapChain()
    {
        var outcome = Outcome<int>.Success(2)
            .Map(x => x + 3)
            .Map(x => x * 4)
            .Map(x => x.ToString());
        return outcome.Data;
    }

    [Benchmark]
    [BenchmarkCategory("Outcome")]
    public int MatchFold()
    {
        return Outcome<int>.Success(9)
            .Match(value => value + 1,
                   () => -1);
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
