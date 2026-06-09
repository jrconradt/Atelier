
namespace Atelier.Framework.Attributes;

[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Class,
    AllowMultiple = true,
    Inherited = true)]
public class RequiresClaimAttribute : Attribute
{
    public string ClaimType { get; }
    public string? ClaimValue { get; set; }
    public string? Description { get; set; }

    public RequiresClaimAttribute(string claimType)
    {
        ClaimType = claimType ?? throw new ArgumentNullException(nameof(claimType));
    }
}
