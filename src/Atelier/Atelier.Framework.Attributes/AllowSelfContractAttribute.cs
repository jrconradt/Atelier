
namespace Atelier.Framework.Attributes;

[AttributeUsage(
    AttributeTargets.Class,
    AllowMultiple = false,
    Inherited = true)]
public class AllowSelfContractAttribute : Attribute
{
    public string IdentityPropertyName { get; set; } = "IdentityId";
    public string? Description { get; set; }

    public AllowSelfContractAttribute()
    {
    }

    public AllowSelfContractAttribute(string identityPropertyName)
    {
        IdentityPropertyName = identityPropertyName ?? throw new ArgumentNullException(nameof(identityPropertyName));
    }
}
