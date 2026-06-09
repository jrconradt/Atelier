using Atelier.Framework.Host.Execution;

namespace Atelier.Framework.Facility;

public class ResourceAvailability
{
    public bool IsAvailable { get; set; }
    public ResourceAllocation Available { get; set; } = new();
    public ResourceAllocation Shortfall { get; set; } = new();
    public TimeSpan? EstimatedWaitTime { get; set; }
}
