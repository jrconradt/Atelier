namespace Atelier.Framework.StateMachine.Service
{
    public class StateMachineInfo
    {
        public string InstanceId { get; set; } = string.Empty;
        public Type Type { get; set; } = typeof(object);
        public string CurrentState { get; set; } = string.Empty;
        public bool IsHealthy { get; set; }
        public DateTime? LastTransition { get; set; }
        public DateTime CreatedAt { get; set; }
        public TimeSpan Uptime => DateTime.UtcNow - CreatedAt;
        public long TransitionCount { get; set; }
        public double TransitionsPerMinute => TransitionCount / Math.Max(Uptime.TotalMinutes, 1);
    }
}
