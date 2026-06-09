using Atelier.Framework.Outcomes;
using Atelier.Framework.Properties;
using Atelier.Framework.StateMachine.Interfaces;
using Atelier.Framework.StateMachine.Service;
using Atelier.Framework.StateMachine.Services;
using Atelier.Framework.Testing;

namespace Atelier.Framework.StateMachine;

[TestFixtureRegistry]
public static class StateMachineTestFixtures
{
    [Fixture(typeof(StateMachineRegistryService), Operation = "UnregisterStateMachineAsync")]
    public static StateMachineRegistryService UnregisterReceiver()
    {
        var service = new StateMachineRegistryService(null, null);
        var instance = new StateMachineInstance<HappyStateMachine>();
        instance.Configure("atelier-happy");
        instance.InitializeAsync().GetAwaiter().GetResult();
        service.RegisterStateMachineAsync("atelier-happy", instance).GetAwaiter().GetResult();
        return service;
    }

    [Fixture(typeof(StateMachineRestoreService), Operation = "RestoreStateMachineAsync")]
    public static StateMachineRestoreService RestoreReceiver()
    {
        var registry = new StateMachineRegistryService(null, null);
        var persistence = AutoMockProvider.For<IStateMachinePersistence>();
        return new StateMachineRestoreService(registry,
                                              persistence,
                                              null,
                                              null);
    }

    [Fixture(typeof(StateMachineSnapshot))]
    public static StateMachineSnapshot Snapshot()
    {
        return new StateMachineSnapshot
        {
            InstanceId = "atelier-happy",
            Type = typeof(HappyStateMachine).AssemblyQualifiedName!,
            Version = StateMachineInstance<HappyStateMachine>.CURRENT_SNAPSHOT_VERSION,
            CurrentState = string.Empty,
            Configuration = new StateMachineConfiguration(),
            Data = null,
            CreatedAt = DateTime.UtcNow,
        };
    }

    public sealed class HappyStateMachine : IStateMachine
    {
        public string CurrentState => "Initial";

        public bool IsTerminal => false;

        public string[] GetValidTransitions()
        {
            return Array.Empty<string>();
        }

        public StateMachineData GetSnapshotData()
        {
            return new StateMachineData();
        }

        public void RestoreFromSnapshot(StateMachineData data)
        {
        }

        public Outcome ExecuteTransition(string transitionName)
        {
            return Outcome.Success();
        }

        public void RegisterStateChangeHandler(Action<string, string> handler)
        {
        }

        public void Configure(StateMachineConfigurationData configuration)
        {
        }
    }
}
