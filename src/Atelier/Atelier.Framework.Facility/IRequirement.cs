using Atelier.Framework.Host.Execution;

namespace Atelier.Framework.Facility;

public interface IRequirement
{
    public string RequirementId { get; }
    public Type RequiredType { get; }
    public RequirementScope Scope { get; }
    public ResourceAllocation ResourceNeeds { get; }
    public Dictionary<string, object> Constraints { get; }
    public FulfillmentPreferences Preferences { get; }
}

public enum RequirementScope
{
    Contract,
    Offering,
    Product,
    Boutique,
    Capability
}

public enum FulfillmentMode
{
    BestFit,
    InternalOnly,
    ExternalPreferred,
    HighestPerformance,
    LowestLatency,
    LowestCost
}
