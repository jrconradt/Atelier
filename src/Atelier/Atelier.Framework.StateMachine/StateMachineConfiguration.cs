using Atelier.Framework.Properties;

namespace Atelier.Framework.StateMachine;

public class StateMachineConfiguration
{
    public string? InitialState { get; set; }
    public StateMachineConfigurationData Properties { get; set; } = new();
    public bool Persist { get; set; } = true;
    public bool Monitor { get; set; } = true;
    public TimeSpan? AutoCleanupTimeout { get; set; }
    public int? MaxTransitionsPerMinute { get; set; }
    public bool EnableEvents { get; set; } = true;
    public Dictionary<string, string>? Tags { get; set; }
}
