using Atelier.Framework.Attributes;
using Atelier.Framework.Host.Execution;

namespace Atelier.Framework.Offering;

[ContractAttribute(
    "CreateOfferingRequest",
    Version = "1.0",
    Namespace = "Framework.Offering")]
public class CreateOfferingRequest
{
    public required string OfferingTypeName { get; set; }
    public OfferingExecutionMode ExecutionMode { get; set; } = OfferingExecutionMode.InProcess;
    public int? TargetProcessId { get; set; }
    public string? NetworkAddress { get; set; }
    public int? NetworkPort { get; set; }
    public bool AutoRegisterDiscovery { get; set; } = true;
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new();
    public ResourceAllocation? ResourceLimits { get; set; }
    public List<string> SecretClaims { get; set; } = new();
}
