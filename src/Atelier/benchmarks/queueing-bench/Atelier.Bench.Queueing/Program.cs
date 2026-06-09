using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Atelier.Framework.Queueing.Core;
using Atelier.Framework.Queueing.Orchestration;

var summary = BenchmarkRunner.Run<QueueingBench>();
BenchmarkResultEmitter.Emit(summary);

[MemoryDiagnoser]
public class QueueingBench
{
    private readonly QueueManager _manager = new();
    private IQueue _queue = null!;

    private readonly QueueMessage _template = new("order.created", "payload")
    {
        Priority = 1,
        MaxRetries = 5
    };

    [GlobalSetup]
    public async Task Setup()
    {
        var queueOutcome = await _manager.CreateQueueAsync("bench-queue").ConfigureAwait(false);
        _queue = queueOutcome.Data!;
    }

    [Benchmark]
    [BenchmarkCategory("Message")]
    public string ConstructMessage()
    {
        var message = new QueueMessage("order.created", "payload");
        return message.MessageType;
    }

    [Benchmark]
    [BenchmarkCategory("Message")]
    public int CreateRetry()
    {
        var retried = _template.CreateRetry();
        return retried.RetryCount;
    }

    [Benchmark]
    [BenchmarkCategory("Message")]
    public int WithUpdates()
    {
        var copy = _template.WithUpdates(m => m.Priority = 2);
        return copy.Priority;
    }

    [Benchmark]
    [BenchmarkCategory("Queue")]
    public async Task<bool> Enqueue()
    {
        var enqueued = await _queue.EnqueueAsync("order.created", "payload").ConfigureAwait(false);
        _queue.Channel.Reader.TryRead(out _);
        return enqueued.IsSuccess;
    }

    [Benchmark]
    [BenchmarkCategory("Queue")]
    public async Task<bool> EnqueueDequeue()
    {
        var enqueued = await _queue.EnqueueAsync("order.created", "payload").ConfigureAwait(false);
        var dequeued = _queue.Channel.Reader.TryRead(out var message)
            && message.MessageType == "order.created";
        return enqueued.IsSuccess && dequeued;
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
