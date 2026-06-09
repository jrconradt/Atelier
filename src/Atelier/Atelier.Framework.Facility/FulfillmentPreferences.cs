using Atelier.Framework.Primitives;
using Atelier.Framework.Network;
using Atelier.Framework.Host.Execution;

namespace Atelier.Framework.Facility;

public class FulfillmentPreferences
{
    public FulfillmentMode Mode { get; set; } = FulfillmentMode.BestFit;
    public string? PreferredFacilityId { get; set; }
    public Type? PreferredZone { get; set; }
    public OfferingExecutionMode? PreferredExecutionMode { get; set; }
    public int? MinInstances { get; set; }
    public int? MaxInstances { get; set; }
}
