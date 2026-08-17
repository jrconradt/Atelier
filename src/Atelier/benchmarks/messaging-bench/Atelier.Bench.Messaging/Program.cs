using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Atelier.Framework.Messaging;

var summary = BenchmarkRunner.Run<MessagingBench>();
BenchmarkResultEmitter.Emit(summary);

[MemoryDiagnoser]
public class MessagingBench
{
    private readonly SingleHandlerFactory _factory = new(new IntegrationPingHandler());
    private readonly HandlerRegistry _registry;

    private readonly IntegrationPingRequest _successRequest = new() { Token = "abc" };
    private readonly IntegrationPingRequest _failureRequest = new() { Token = string.Empty };
    private readonly IntegrationPongResponse _unmatchedRequest = new() { Echo = "unmatched" };

    public MessagingBench()
    {
        _registry = new HandlerRegistry(_factory,
                                        null);
    }

    [Benchmark]
    [BenchmarkCategory("Dispatch")]
    public async Task<bool> DispatchSuccess()
    {
        var outcome = await _registry
            .HandleAsync<IntegrationPingRequest, IntegrationPongResponse>(_successRequest)
            .ConfigureAwait(false);
        return outcome.IsSuccess;
    }

    [Benchmark]
    [BenchmarkCategory("Dispatch")]
    public async Task<bool> DispatchHandledFailure()
    {
        var outcome = await _registry
            .HandleAsync<IntegrationPingRequest, IntegrationPongResponse>(_failureRequest)
            .ConfigureAwait(false);
        return outcome.IsSuccess;
    }

    [Benchmark]
    [BenchmarkCategory("Dispatch")]
    public async Task<bool> DispatchUnregistered()
    {
        var outcome = await _registry
            .HandleAsync<IntegrationPongResponse, IntegrationPingRequest>(_unmatchedRequest)
            .ConfigureAwait(false);
        return outcome.IsSuccess;
    }

    [Benchmark]
    [BenchmarkCategory("Resolution")]
    public bool FactoryResolve()
    {
        var handler = _factory.GetHandler<IntegrationPingRequest, IntegrationPongResponse>();
        return handler is not null;
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
