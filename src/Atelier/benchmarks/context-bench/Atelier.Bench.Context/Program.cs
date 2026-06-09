using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Atelier.Framework.Context;

var summary = BenchmarkRunner.Run<ContextBench>();
BenchmarkResultEmitter.Emit(summary);

[MemoryDiagnoser]
public class ContextBench
{
    private readonly CompactContextSerializer _serializer;
    private readonly string _serializedContext;

    public ContextBench()
    {
        var signingKey = new byte[32];
        for (int k = 0; k < signingKey.Length; k++)
        {
            signingKey[k] = (byte)(k + 1);
        }
        _serializer = new CompactContextSerializer(signingKey);
        _serializedContext = _serializer.Serialize(NewContext());
    }

    private static CompositeContext NewContext()
    {
        var context = new CompositeContext(Guid.NewGuid().ToString(),
                                           "checkout")
        {
            Scope = ContextScope.Service,
            ServiceId = "orders",
            DomainId = "commerce",
            CorrelationId = "corr-123"
        };
        context.AddValue("tenant",
                         "acme");
        return context;
    }

    [Benchmark]
    [BenchmarkCategory("Context")]
    public string? Create()
    {
        var context = NewContext();
        return context.ServiceId;
    }

    [Benchmark]
    [BenchmarkCategory("Serialization")]
    public int Serialize()
    {
        var wire = _serializer.Serialize(NewContext());
        return wire.Length;
    }

    [Benchmark]
    [BenchmarkCategory("Serialization")]
    public string? Deserialize()
    {
        var context = _serializer.Deserialize(_serializedContext);
        return context.ServiceId;
    }

    [Benchmark]
    [BenchmarkCategory("Serialization")]
    public string? RoundTrip()
    {
        var wire = _serializer.Serialize(NewContext());
        var context = _serializer.Deserialize(wire);
        return context.CorrelationId;
    }

    [Benchmark]
    [BenchmarkCategory("Context")]
    public bool SetAndReadValue()
    {
        var context = NewContext();
        context.AddValue("region",
                         "us-east");
        return context.TryGetValue("region", out var value)
            && value == "us-east";
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
