namespace Atelier.Framework.Host.Execution;

public class HostExecutionContext
{
    public string InstanceId { get; set; } = Guid.NewGuid().ToString();
    public Type? OfferingType { get; set; }
    public string OfferingTypeName { get; set; } = string.Empty;
    public OfferingExecutionMode ExecutionMode { get; set; }
    public HostState State { get; set; } = HostState.Pending;

    public IHost? Host { get; set; }
    public object? OfferingInstance { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? StoppedAt { get; set; }

    public string? NetworkAddress { get; set; }
    public int? NetworkPort { get; set; }
    public int? ProcessId { get; set; }

    public Dictionary<string, string> Metadata { get; set; } = new();
    public ResourceAllocation ResourceAllocation { get; set; } = new();

    public CancellationTokenSource? CancellationTokenSource { get; set; }

    public string? FailureReason { get; set; }
}
