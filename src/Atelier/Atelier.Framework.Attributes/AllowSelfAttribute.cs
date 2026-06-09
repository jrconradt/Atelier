
namespace Atelier.Framework.Attributes;

[AttributeUsage(
    AttributeTargets.Method,
    AllowMultiple = false,
    Inherited = true)]
public class AllowSelfAttribute : Attribute
{
    public string IdentityParameterName { get; set; } = "identityId";
    public string? Description { get; set; }

    public AllowSelfAttribute()
    {
    }

    public AllowSelfAttribute(string identityParameterName)
    {
        IdentityParameterName = identityParameterName ?? throw new ArgumentNullException(nameof(identityParameterName));
    }
}
