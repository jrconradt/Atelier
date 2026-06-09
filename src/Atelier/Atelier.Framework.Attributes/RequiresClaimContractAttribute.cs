
namespace Atelier.Framework.Attributes;

[AttributeUsage(
    AttributeTargets.Class,
    AllowMultiple = true,
    Inherited = true)]
public class RequiresClaimContractAttribute : Attribute
{
    public string ClaimType { get; }
    public string? ClaimValue { get; set; }
    public string? Description { get; set; }

    public RequiresClaimContractAttribute(string claimType)
    {
        ClaimType = claimType ?? throw new ArgumentNullException(nameof(claimType));
    }
}
