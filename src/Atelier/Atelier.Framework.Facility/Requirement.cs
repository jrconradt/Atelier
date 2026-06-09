using Atelier.Framework.Host.Execution;

namespace Atelier.Framework.Facility;

public class Requirement<T> : IRequirement where T : class
{
    public string RequirementId { get; set; } = Guid.NewGuid().ToString();
    public Type RequiredType => typeof(T);
    public RequirementScope Scope { get; set; }
    public ResourceAllocation ResourceNeeds { get; set; } = new();
    public Dictionary<string, object> Constraints { get; set; } = new();
    public FulfillmentPreferences Preferences { get; set; } = new();
}
