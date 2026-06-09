namespace Atelier.Framework.Facility;

public class CapabilityDependency
{
    public string DependencyId { get; set; } = string.Empty;
    public string DependencyType { get; set; } = string.Empty;
    public bool IsRequired { get; set; } = true;
    public string Version { get; set; } = string.Empty;
}
