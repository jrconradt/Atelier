using Atelier.Framework.Attributes;
using Atelier.Framework.Outcomes;
using Atelier.Framework.StateMachine.Interfaces;
using Atelier.Framework.StateMachine.Service;

namespace Atelier.Framework.StateMachine.Services;

public interface IStateMachineRegistryService
{
    Task<Outcome<T>> GetStateMachineAsync<T>(
        string instanceId,
        CancellationToken cancellationToken = default)
        where T : class, IStateMachine;

    Task<Outcome<IEnumerable<StateMachineInfo>>> GetAllStateMachinesAsync(
        CancellationToken cancellationToken = default);

    IEnumerable<IStateMachineInstance> GetAllInstances();

    IStateMachineInstance? GetInstance(string instanceId);

    Task<Outcome> RegisterStateMachineAsync(
        string instanceId,
        IStateMachineInstance instance,
        CancellationToken cancellationToken = default);

    Task<Outcome> UnregisterStateMachineAsync(
        string instanceId,
        CancellationToken cancellationToken = default);
}