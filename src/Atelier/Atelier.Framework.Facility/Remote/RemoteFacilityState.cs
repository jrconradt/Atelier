namespace Atelier.Framework.Facility.Remote;

internal sealed class RemoteFacilityState
{
    public RemoteFacilityDescriptor Descriptor { get; set; } = new();
    public RemoteFacilityHealthProbe? HealthProbe { get; set; }
}
