using Atelier.Framework.Attributes;

namespace Atelier.Framework.Attache;

[Contract("AttacheConfigurationInternal", Version = "1.0", Namespace = "Framework.Attache")]
public class AttacheConfiguration
{
    public bool AutoStartBoutiques { get; set; } = true;
    public AttacheResourceLimits ResourceLimits { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new();
}

public class AttacheResourceLimits
{
    public long? MaxMemoryBytes { get; set; }
    public int? MaxCpuPercent { get; set; }
    public int? MaxBoutiques { get; set; }
}
