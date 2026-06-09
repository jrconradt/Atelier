
namespace Atelier.Framework.Attributes;

[AttributeUsage(
    AttributeTargets.Class,
    AllowMultiple = true,
    Inherited = true)]
public class RequiresScopeContractAttribute : Attribute
{
    public string Scope { get; }
    public string? Description { get; set; }

    public RequiresScopeContractAttribute(string scope)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
    }
}
