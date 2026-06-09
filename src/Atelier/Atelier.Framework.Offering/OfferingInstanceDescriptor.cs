using Atelier.Framework.Attributes;
using Atelier.Framework.Host.Execution;

namespace Atelier.Framework.Offering;

[ContractAttribute(
    "OfferingInstanceDescriptor",
    Version = "1.0",
    Namespace = "Framework.Offering")]
public class OfferingInstanceDescriptor
{
    public string InstanceId { get; set; } = Guid.NewGuid().ToString();
    public Type? OfferingType { get; set; }
    public string OfferingTypeName { get; set; } = string.Empty;
    public OfferingExecutionMode ExecutionMode { get; set; }
    public OfferingInstanceState State { get; set; } = OfferingInstanceState.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? StoppedAt { get; set; }
    public int? ProcessId { get; set; }
    public string? NetworkAddress { get; set; }
    public int? NetworkPort { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
    public ResourceAllocation ResourceAllocation { get; set; } = new();
    public string? FailureReason { get; set; }
}
