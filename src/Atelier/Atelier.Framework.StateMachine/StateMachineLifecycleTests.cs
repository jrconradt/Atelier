using Atelier.Framework.Outcomes;
using Atelier.Framework.Properties;
using Atelier.Framework.StateMachine.Interfaces;
using Atelier.Framework.StateMachine.Service;
using Atelier.Framework.Testing;

namespace Atelier.Framework.StateMachine;

public static class StateMachineLifecycleTests
{
    private const string INSTANCE_TARGET = "global::Atelier.Framework.StateMachine.StateMachineInstance`1";
    private const string REGISTRY_TARGET = "global::Atelier.Framework.StateMachine.Service.StateMachineRegistry";

    private sealed class FakeStateMachine : IStateMachine
    {
        public string CurrentState { get; private set; } = "Initial";
        public bool IsTerminal { get; private set; }

        public string[] GetValidTransitions()
        {
            return new[] { "Advance" };
        }

        public StateMachineData GetSnapshotData()
        {
            var data = new StateMachineData();
            data.CurrentState = CurrentState;
            return data;
        }

        public void RestoreFromSnapshot(StateMachineData data)
        {
            CurrentState = data.CurrentState ?? "Initial";
        }

        public Outcome ExecuteTransition(string transitionName)
        {
            if (transitionName != "Advance")
            {
                return Outcome.Failure();
            }

            CurrentState = "Advanced";
            IsTerminal = true;
            return Outcome.Success();
        }

        public void RegisterStateChangeHandler(Action<string, string> handler)
        {
        }

        public void Configure(StateMachineConfigurationData configuration)
        {
        }
    }

    private sealed class OtherStateMachine : IStateMachine
    {
        public string CurrentState => "Initial";
        public bool IsTerminal => false;
        public string[] GetValidTransitions() => Array.Empty<string>();
        public StateMachineData GetSnapshotData() => new();
        public void RestoreFromSnapshot(StateMachineData data) { }
        public Outcome ExecuteTransition(string transitionName) => Outcome.Success();
        public void RegisterStateChangeHandler(Action<string, string> handler) { }
        public void Configure(StateMachineConfigurationData configuration) { }
    }

    private static void IsTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    [GeneratedTest("statemachine.transition.invalid-rejected", INSTANCE_TARGET)]
    public static async Task InvalidTransitionIsRejected()
    {
        var instance = new StateMachineInstance<FakeStateMachine>();
        instance.Configure("invalid-transition");
        var init = await instance.InitializeAsync();
        IsTrue(init.IsSuccess, "Initialize failed");

        var result = await instance.ExecuteTransitionAsync("DoesNotExist");
        IsTrue(!result.IsSuccess, "Unknown transition was not rejected");
        IsTrue(!instance.IsHealthy, "Instance should be unhealthy after a failed transition");
    }

    [GeneratedTest("statemachine.snapshot.version-newer-rejected", INSTANCE_TARGET)]
    public static async Task SnapshotNewerThanCurrentIsRejected()
    {
        var instance = new StateMachineInstance<FakeStateMachine>();
        instance.Configure("version-newer");
        await instance.InitializeAsync();

        var snapshot = new StateMachineSnapshot
        {
            InstanceId = "version-newer",
            Type = typeof(FakeStateMachine).AssemblyQualifiedName!,
            Version = StateMachineInstance<FakeStateMachine>.CURRENT_SNAPSHOT_VERSION + 1
        };

        var result = instance.RestoreFromSnapshot(snapshot);
        IsTrue(!result.IsSuccess, "Newer snapshot version was accepted");
    }

    [GeneratedTest("statemachine.snapshot.type-mismatch-rejected", INSTANCE_TARGET)]
    public static async Task SnapshotTypeMismatchIsRejected()
    {
        var instance = new StateMachineInstance<FakeStateMachine>();
        instance.Configure("type-mismatch");
        await instance.InitializeAsync();

        var snapshot = new StateMachineSnapshot
        {
            InstanceId = "type-mismatch",
            Type = typeof(OtherStateMachine).AssemblyQualifiedName!,
            Version = StateMachineInstance<FakeStateMachine>.CURRENT_SNAPSHOT_VERSION
        };

        var result = instance.RestoreFromSnapshot(snapshot);
        IsTrue(!result.IsSuccess, "Mismatched snapshot type was accepted");
    }

    [GeneratedTest("statemachine.snapshot.data-missing-rejected", INSTANCE_TARGET)]
    public static async Task SnapshotDeclaringStateWithoutDataIsRejected()
    {
        var instance = new StateMachineInstance<FakeStateMachine>();
        instance.Configure("data-missing");
        await instance.InitializeAsync();

        var snapshot = new StateMachineSnapshot
        {
            InstanceId = "data-missing",
            Type = typeof(FakeStateMachine).AssemblyQualifiedName!,
            Version = StateMachineInstance<FakeStateMachine>.CURRENT_SNAPSHOT_VERSION,
            CurrentState = "Advanced",
            Data = null
        };

        var result = instance.RestoreFromSnapshot(snapshot);
        IsTrue(!result.IsSuccess, "Snapshot declaring state with no data was accepted");
    }

    [GeneratedTest("statemachine.registry.duplicate-replaces-index", REGISTRY_TARGET)]
    public static async Task RegisterAndUnregisterMaintainsCount()
    {
        var registry = new StateMachineRegistry();
        var instance = new StateMachineInstance<FakeStateMachine>();
        instance.Configure("registry-one");
        await instance.InitializeAsync();

        var registerResult = await registry.Register("registry-one", instance);
        IsTrue(registerResult.IsSuccess, "Register failed");
        IsTrue(registry.Count == 1, $"Expected count 1, saw {registry.Count}");

        var lookup = await registry.GetInstance("registry-one");
        IsTrue(lookup.IsSuccess, "Registered instance not found");

        var unregister = await registry.Unregister("registry-one");
        IsTrue(unregister.IsSuccess, "Unregister failed");
        IsTrue(registry.Count == 0, $"Expected count 0, saw {registry.Count}");
    }

    [GeneratedTest("statemachine.registry.missing-id-not-found", REGISTRY_TARGET)]
    public static async Task GetMissingInstanceReturnsNotFound()
    {
        var registry = new StateMachineRegistry();
        var lookup = await registry.GetInstance("absent");
        IsTrue(!lookup.IsSuccess, "Missing instance lookup unexpectedly succeeded");

        var unregister = await registry.Unregister("absent");
        IsTrue(unregister.IsSuccess, "Unregister of absent instance should be idempotent success");
    }
}
