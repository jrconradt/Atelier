using Atelier.Framework.Attributes;

namespace Atelier.Framework.Facility.Configuration;

[Contract("FacilityConfiguration", Version = "1.0", Namespace = "Framework.Facility.Configuration")]
public class FacilityConfiguration
{
    public const string SECTION_NAME = "Facility";

    public RemoteFacilityConfiguration Remote { get; set; } = new();
}

[Contract("RemoteFacilityConfiguration", Version = "1.0", Namespace = "Framework.Facility.Configuration")]
public class RemoteFacilityConfiguration
{
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRetries { get; set; } = 3;
    public int RetryDelayMilliseconds { get; set; } = 1000;
}
