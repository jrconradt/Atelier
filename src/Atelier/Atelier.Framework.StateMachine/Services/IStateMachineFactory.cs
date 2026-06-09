using Atelier.Framework.Attributes;
using Atelier.Framework.Outcomes;
using Atelier.Framework.StateMachine.Interfaces;

namespace Atelier.Framework.StateMachine.Services;

public interface IStateMachineFactory
{
    Task<Outcome<T>> CreateStateMachineAsync<T>(
        string instanceId,
        StateMachineConfiguration config,
        CancellationToken cancellationToken = default)
        where T : class, IStateMachine;

    Task<Outcome> DestroyStateMachineAsync(
        string instanceId,
        CancellationToken cancellationToken = default);
}