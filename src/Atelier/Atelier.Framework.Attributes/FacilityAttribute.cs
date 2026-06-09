namespace Atelier.Framework.Attributes;

[AttributeUsage(
    AttributeTargets.Interface,
    AllowMultiple = false,
    Inherited = false)]
public class FacilityAttribute : Attribute
{
    public string Name { get; }
    public string Version { get; set; }
    public string? Description { get; set; }
    public string[]? RequiredScopes { get; set; }
    public string[]? RequiredClaims { get; set; }
    public bool RequiresAuthentication { get; set; }
    public bool AllowAnonymous { get; set; }

    public FacilityAttribute(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        Version = "1.0";
        RequiresAuthentication = true;
        AllowAnonymous = false;
    }
}
