using Atelier.Framework.Outcomes;

namespace Atelier.Framework.StateMachine.Service
{
    public interface IStateMachineMonitor
    {
        public Task<Outcome> CheckHealthAsync(IStateMachineInstance instance, CancellationToken cancellationToken = default);
        public Task<Outcome> RecordTransitionAsync(IStateMachineInstance instance, string transitionName, CancellationToken cancellationToken = default);
        public Task<Outcome> RecordStateChangeAsync(IStateMachineInstance instance, string fromState, string toState);
        public Task<Outcome> RecordErrorAsync(IStateMachineInstance instance, Exception exception, CancellationToken cancellationToken = default);
        public Task<Outcome<StateMachineMetrics>> GetMetricsAsync(IStateMachineInstance instance, CancellationToken cancellationToken = default);
        public Task<Outcome<IEnumerable<StateMachineMetrics>>> GetAllMetricsAsync(CancellationToken cancellationToken = default);
    }
}
