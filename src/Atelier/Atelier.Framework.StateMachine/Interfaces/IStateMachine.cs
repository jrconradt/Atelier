using Atelier.Framework.Outcomes;
using Atelier.Framework.Properties;

namespace Atelier.Framework.StateMachine.Interfaces;

public interface IStateMachine
{
    public string CurrentState { get; }
    public bool IsTerminal { get; }
    public string[] GetValidTransitions();
    public StateMachineData GetSnapshotData();
    public void RestoreFromSnapshot(StateMachineData data);
    public Outcome ExecuteTransition(string transitionName);
    public void RegisterStateChangeHandler(Action<string, string> handler);
    public void Configure(StateMachineConfigurationData configuration);
}
