namespace Atelier.Framework.Attributes;

[AttributeUsage(
    AttributeTargets.Class,
    AllowMultiple = false,
    Inherited = false)]
public class ContractAttribute : Attribute
{
    public string Name { get; }
    public string Version { get; set; }
    public string? Namespace { get; set; }
    public bool IsBackwardCompatible { get; set; }
    public string[]? RequiredScopes { get; set; }
    public string[]? RequiredClaims { get; set; }
    public bool RequiresAuthentication { get; set; }
    public bool AllowAnonymous { get; set; }

    public ContractAttribute(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        Version = "1.0";
        IsBackwardCompatible = false;
        RequiresAuthentication = true;
        AllowAnonymous = false;
    }
}

