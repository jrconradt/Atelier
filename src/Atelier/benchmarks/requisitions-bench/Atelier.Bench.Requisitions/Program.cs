using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Atelier.Bench.Requisitions;

var summary = BenchmarkRunner.Run<RequisitionsBench>();
BenchmarkResultEmitter.Emit(summary);

[MemoryDiagnoser]
public class RequisitionsBench
{
    private readonly PrimaryDependency _primary = new();
    private readonly SecondaryDependency _secondary = new();

    [Benchmark]
    [BenchmarkCategory("Wiring")]
    public bool GeneratedConstructorInjection()
    {
        var service = new WiredService(_primary,
                                       _secondary);
        return ReferenceEquals(service.Primary, _primary)
            && ReferenceEquals(service.Secondary, _secondary);
    }

    [Benchmark]
    [BenchmarkCategory("Wiring")]
    public bool OptionalRequisiteOmitted()
    {
        var service = new WiredService(_primary,
                                       null);
        return service.Secondary is null;
    }

    [Benchmark]
    [BenchmarkCategory("Wiring")]
    public bool RequiredRequisiteRejected()
    {
        try
        {
            _ = new WiredService(null!,
                                 _secondary);
        }
        catch (ArgumentNullException)
        {
            return true;
        }
        return false;
    }

    [Benchmark]
    [BenchmarkCategory("Resolution")]
    public string RequisiteResolution()
    {
        var service = new WiredService(_primary,
                                       _secondary);
        return service.Resolve();
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
