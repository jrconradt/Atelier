using Atelier.Framework.Attributes;
using Atelier.Framework.Outcomes;
using Atelier.Framework.StateMachine.Interfaces;

namespace Atelier.Framework.StateMachine.Services;

public interface IStateMachineTransitionService
{
    Task<Outcome> TransitionStateMachineAsync(
        string instanceId,
        string transitionName,
        CancellationToken cancellationToken = default);

    Task<Outcome<IEnumerable<string>>> GetValidTransitionsAsync(
        string instanceId,
        CancellationToken cancellationToken = default);
}