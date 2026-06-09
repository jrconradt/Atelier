using Atelier.Framework.Properties;

namespace Atelier.Framework.StateMachine.Service
{
    public class StateMachineSnapshot
    {
        public string InstanceId { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string CurrentState { get; set; } = string.Empty;
        public StateMachineConfiguration? Configuration { get; set; }
        public DateTime? LastTransition { get; set; }
        public DateTime CreatedAt { get; set; }
        public StateMachineData? Data { get; set; }
        public DateTime SnapshotAt { get; set; } = DateTime.UtcNow;
        public int Version { get; set; } = 1;
    }
}
