using Atelier.Framework.Primitives;
using Atelier.Framework.Network;
using Atelier.Framework.Host.Execution;

namespace Atelier.Framework.Facility;

public class FacilityCapabilities
{
    public HashSet<RequirementScope> SupportedScopes { get; set; } = new();
    public HashSet<Type> CanProvide { get; set; } = new();
    public ResourceAllocation TotalCapacity { get; set; } = new();
    public ResourceAllocation CurrentAvailable { get; set; } = new();
    public Type? Zone { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();

    public HashSet<string> Operations { get; set; } = new();
    public Dictionary<string, CapabilityContract> Contracts { get; set; } = new();
    public List<CapabilityDependency> Dependencies { get; set; } = new();
    public SecurityRequirements Security { get; set; } = new();
}
