
using Atelier.Framework.Outcomes;

namespace Atelier.Framework.StateMachine.Service
{
    public interface IStateMachineInstance : IAsyncDisposable
    {
        public string InstanceId { get; }
        public Type Type { get; }
        public string CurrentState { get; }
        public bool IsHealthy { get; }
        public DateTime? LastTransition { get; }
        public DateTime CreatedAt { get; }
        public IReadOnlyDictionary<string, string> Tags { get; }

        public Task<Outcome> ExecuteTransitionAsync(string transitionName, CancellationToken cancellationToken = default);
        public Task<Outcome<StateMachineSnapshot>> CreateSnapshot();
        public IEnumerable<string> GetValidTransitions();
    }
}
