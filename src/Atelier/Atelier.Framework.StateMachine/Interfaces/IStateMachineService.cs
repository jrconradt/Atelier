
using Atelier.Framework.Outcomes;
using Atelier.Framework.StateMachine.Interfaces;

namespace Atelier.Framework.StateMachine.Service;

public interface IStateMachineManager
{
    public Task<Outcome<T>> CreateStateMachineAsync<T>(
        string instanceId,
        StateMachineConfiguration config,
        CancellationToken cancellationToken = default) where T : class, IStateMachine;
    public Task<Outcome> DestroyStateMachineAsync(string instanceId, CancellationToken cancellationToken = default);
    public Task<Outcome<T>> GetStateMachineAsync<T>(string instanceId, CancellationToken cancellationToken = default) where T : class, IStateMachine;
    public Task<Outcome> TransitionStateMachineAsync(
        string instanceId,
        string transitionName,
        CancellationToken cancellationToken = default);
    public Task<Outcome<IEnumerable<StateMachineInfo>>> GetAllStateMachinesAsync(CancellationToken cancellationToken = default);
    public Task<Outcome> RestoreStateMachineAsync(
        string instanceId,
        StateMachineSnapshot snapshot,
        CancellationToken cancellationToken = default);
}
