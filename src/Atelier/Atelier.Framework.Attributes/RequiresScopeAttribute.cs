
namespace Atelier.Framework.Attributes;

[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Class,
    AllowMultiple = true,
    Inherited = true)]
public class RequiresScopeAttribute : Attribute
{
    public string Scope { get; }
    public string? Description { get; set; }

    public RequiresScopeAttribute(string scope)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
    }
}
