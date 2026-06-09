using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Atelier.Framework.Outcomes;
using Atelier.Framework.StateMachine;
using Atelier.Framework.StateMachine.Service;

var summary = BenchmarkRunner.Run<StateMachineBench>();
BenchmarkResultEmitter.Emit(summary);

[MemoryDiagnoser]
public class StateMachineBench
{
    private readonly StateMachineRegistry _registry = new();
    private readonly ProbeStateMachineInstance _resident = new("resident",
                                                               "order",
                                                               "pending");

    [GlobalSetup]
    public async Task Setup()
    {
        await _registry.Register(_resident.InstanceId, _resident).ConfigureAwait(false);
    }

    [Benchmark]
    [BenchmarkCategory("Registry")]
    public async Task<bool> RegisterUnregister()
    {
        var instance = new ProbeStateMachineInstance("transient",
                                                     "order",
                                                     "pending");
        var registered = await _registry.Register(instance.InstanceId, instance).ConfigureAwait(false);
        var removed = await _registry.Unregister(instance.InstanceId).ConfigureAwait(false);
        return registered.IsSuccess && removed.IsSuccess;
    }

    [Benchmark]
    [BenchmarkCategory("Registry")]
    public async Task<bool> LookupHit()
    {
        var outcome = await _registry.GetInstance(_resident.InstanceId).ConfigureAwait(false);
        return outcome.IsSuccess
            && ReferenceEquals(outcome.Data, _resident);
    }

    [Benchmark]
    [BenchmarkCategory("Registry")]
    public async Task<bool> LookupMiss()
    {
        var outcome = await _registry.GetInstance("absent").ConfigureAwait(false);
        return outcome.IsSuccess;
    }

    [Benchmark]
    [BenchmarkCategory("Registry")]
    public async Task<bool> QueryByTag()
    {
        var outcome = await _registry.GetInstancesByTag("region", "us-east").ConfigureAwait(false);
        return outcome.IsSuccess
            && outcome.Data!.Any();
    }

    [Benchmark]
    [BenchmarkCategory("Lifecycle")]
    public async Task<bool> Transition()
    {
        var outcome = await _resident.ExecuteTransitionAsync("advance").ConfigureAwait(false);
        return outcome.IsSuccess;
    }

    [Benchmark]
    [BenchmarkCategory("Lifecycle")]
    public async Task<bool> Snapshot()
    {
        var outcome = await _resident.CreateSnapshot().ConfigureAwait(false);
        return outcome.IsSuccess
            && outcome.Data!.InstanceId == _resident.InstanceId;
    }
}

internal sealed class ProbeStateMachineInstance : IStateMachineInstance
{
    private static readonly string[] States = ["pending", "active", "complete"];

    private readonly string _kind;
    private int _stateIndex;

    public ProbeStateMachineInstance(
        string instanceId,
        string kind,
        string initialState)
    {
        InstanceId = instanceId;
        _kind = kind;
        _stateIndex = Array.IndexOf(States, initialState);
        if (_stateIndex < 0)
        {
            _stateIndex = 0;
        }
        Tags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["kind"] = kind,
            ["region"] = "us-east"
        };
    }

    public string InstanceId { get; }
    public Type Type => typeof(ProbeStateMachineInstance);
    public string CurrentState => States[_stateIndex];
    public bool IsHealthy => true;
    public DateTime? LastTransition { get; private set; }
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
    public IReadOnlyDictionary<string, string> Tags { get; }

    public Task<Outcome> ExecuteTransitionAsync(
        string transitionName,
        CancellationToken cancellationToken = default)
    {
        if (transitionName is null)
        {
            return Task.FromResult(Outcome.Failure());
        }

        _stateIndex = (_stateIndex + 1) % States.Length;
        LastTransition = DateTime.UtcNow;
        return Task.FromResult(Outcome.Success());
    }

    public Task<Outcome<StateMachineSnapshot>> CreateSnapshot()
    {
        return Task.FromResult(Outcome<StateMachineSnapshot>.Success(new StateMachineSnapshot
        {
            InstanceId = InstanceId,
            Type = Type.AssemblyQualifiedName!,
            CurrentState = CurrentState,
            LastTransition = LastTransition,
            CreatedAt = CreatedAt,
            Configuration = new StateMachineConfiguration
            {
                Tags = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["kind"] = _kind
                }
            }
        }));
    }

    public IEnumerable<string> GetValidTransitions()
    {
        return ["advance"];
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
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
