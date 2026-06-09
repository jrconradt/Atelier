using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Atelier.Framework.EventStream;

var summary = BenchmarkRunner.Run<EventStreamBench>();
BenchmarkResultEmitter.Emit(summary);

[MemoryDiagnoser]
public class EventStreamBench
{
    private readonly byte[] _blob = Encoding.UTF8.GetBytes("event-payload-0123456789");
    private readonly string _hash;
    private readonly HashRegistryStore _touchStore = new();

    public EventStreamBench()
    {
        _hash = Convert.ToHexStringLower(SHA256.HashData(_blob));
        _touchStore.Register(_hash,
                             _blob);
    }

    [Benchmark]
    [BenchmarkCategory("HashRegistry")]
    public bool RegisterAndRelease()
    {
        var store = new HashRegistryStore();
        var registered = store.Register(_hash,
                                        _blob);
        var released = store.Release(_hash);
        return registered.Status == HashRegisterStatus.Registered
            && released.Removed;
    }

    [Benchmark]
    [BenchmarkCategory("HashRegistry")]
    public bool TouchHit()
    {
        return _touchStore.TryTouch(_hash, out var found)
            && found is not null;
    }

    [Benchmark]
    [BenchmarkCategory("HashRegistry")]
    public bool ContainsHit()
    {
        return _touchStore.Contains(_hash);
    }

    [Benchmark]
    [BenchmarkCategory("HashRegistry")]
    public int ReferenceCountRead()
    {
        return _touchStore.GetReferenceCount(_hash);
    }

    [Benchmark]
    [BenchmarkCategory("HashRegistry")]
    public int RefCountChurn()
    {
        var store = new HashRegistryStore();
        store.Register(_hash,
                       _blob);
        store.Register(_hash,
                       _blob);
        var released = store.Release(_hash);
        return released.RefCount;
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
