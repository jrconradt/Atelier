using Atelier.Framework.Attributes;
using Atelier.Framework.Outcomes;
using Atelier.Framework.StateMachine.Interfaces;
using Atelier.Framework.StateMachine.Service;

namespace Atelier.Framework.StateMachine.Services;

public interface IStateMachineRestoreService
{
    Task<Outcome> RestoreStateMachineAsync(
        string instanceId,
        StateMachineSnapshot snapshot,
        CancellationToken cancellationToken = default);

    Task<Outcome> RestoreAllPersistedMachinesAsync(
        CancellationToken cancellationToken = default);
}