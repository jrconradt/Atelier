
namespace Atelier.Framework.Offering.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ProductOfferingAttribute : Attribute
{
    public Type ProductType { get; }

    public ProductOfferingAttribute(Type productType)
    {
        ArgumentNullException.ThrowIfNull(productType);
        ProductType = productType;
    }
}
