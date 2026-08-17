using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Atelier.Framework.Context;
using Atelier.Framework.Network;

var summary = BenchmarkRunner.Run<NetworkBench>();
BenchmarkResultEmitter.Emit(summary);

[MemoryDiagnoser]
public class NetworkBench
{
    private readonly string _encoded;

    public NetworkBench()
    {
        _encoded = WireContextCodec.Encode(NewContext()) ?? throw new InvalidOperationException("encode produced null");
    }

    private static global::Atelier.Framework.Context.Context NewContext()
    {
        var context = new global::Atelier.Framework.Context.Context(Guid.NewGuid().ToString(),
                                           "edge")
        {
            TraceId = "trace-abc",
            SpanId = "span-def",
            CorrelationId = "corr-123"
        };
        context.Authorization = AuthorizationContext.Create("user-1",
                                                            "tenant-1",
                                                            "session-1");
        return context;
    }

    [Benchmark]
    [BenchmarkCategory("Wire")]
    public string? WireEncode()
    {
        return WireContextCodec.Encode(NewContext());
    }

    [Benchmark]
    [BenchmarkCategory("Wire")]
    public string? WireDecode()
    {
        var context = WireContextCodec.Decode(_encoded);
        return context?.CorrelationId;
    }

    [Benchmark]
    [BenchmarkCategory("Wire")]
    public string? WireRoundTrip()
    {
        var wire = WireContextCodec.Encode(NewContext());
        var context = WireContextCodec.Decode(wire);
        return context?.TraceId;
    }

    [Benchmark]
    [BenchmarkCategory("Wire")]
    public string RedactIdentifier()
    {
        return WireContextCodec.RedactIdentifier("user-1");
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
