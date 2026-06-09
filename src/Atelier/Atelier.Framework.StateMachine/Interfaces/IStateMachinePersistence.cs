
using Atelier.Framework.Outcomes;

namespace Atelier.Framework.StateMachine.Service
{
    public interface IStateMachinePersistence
    {
        public Task<Outcome> SaveSnapshotAsync(StateMachineSnapshot snapshot, CancellationToken cancellationToken = default);
        public Task<Outcome<StateMachineSnapshot>> LoadSnapshotAsync(string instanceId, CancellationToken cancellationToken = default);
        public Task<Outcome<IEnumerable<StateMachineSnapshot>>> GetAllSnapshotsAsync(CancellationToken cancellationToken = default);
        public Task<Outcome> DeleteSnapshotAsync(string instanceId, CancellationToken cancellationToken = default);
        public Task<Outcome> CleanupSnapshotsAsync(TimeSpan olderThan, CancellationToken cancellationToken = default);
    }
}
