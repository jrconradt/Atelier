using Atelier.Framework.Attributes;

namespace Atelier.Framework.Host.Execution;

[ContractAttribute(
    "BackingOfferingDescriptor",
    Version = "1.0",
    Namespace = "Framework.System.Execution")]
public class BackingOfferingDescriptor
{
    public string ImageName { get; set; } = string.Empty;
    public string? ContainerNamePrefix { get; set; }
    public List<int> ExposedPorts { get; set; } = new();
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new();
    public Dictionary<string, string> Labels { get; set; } = new();
    public int ReadyProbePort { get; set; }
    public ResourceAllocation? ResourceLimits { get; set; }
}
