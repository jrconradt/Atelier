using Atelier.Framework.Attributes;
using Atelier.Framework.Outcomes;
using Atelier.Framework.StateMachine.Interfaces;

namespace Atelier.Framework.StateMachine.Services;

public interface IStateMachineMonitoringService
{
    Task<Outcome> StartMonitoringAsync(CancellationToken cancellationToken = default);
    Task<Outcome> StopMonitoringAsync(CancellationToken cancellationToken = default);
    Task<Outcome> MonitorStateMachinesAsync(CancellationToken cancellationToken = default);
    Task<Outcome> CreateSnapshotAsync(string instanceId, CancellationToken cancellationToken = default);
}