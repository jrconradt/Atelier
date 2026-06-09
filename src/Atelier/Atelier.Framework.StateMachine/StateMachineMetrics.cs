namespace Atelier.Framework.StateMachine.Service
{
    public class StateMachineMetrics
    {
        public string InstanceId { get; set; } = string.Empty;
        public Type Type { get; set; } = typeof(object);
        public string CurrentState { get; set; } = string.Empty;
        public bool IsHealthy { get; set; }
        public long TotalTransitions { get; set; }
        public double TransitionsPerMinute { get; set; }
        public TimeSpan AverageTransitionInterval { get; set; }
        public long ErrorCount { get; set; }
        public DateTime? LastError { get; set; }
        public TimeSpan Uptime { get; set; }
        public long? MemoryUsageBytes { get; set; }
        public DateTime? LastTransition { get; set; }
        public TimeSpan? TimeSinceLastTransition => LastTransition.HasValue ? DateTime.UtcNow - LastTransition.Value : null;
    }
}
