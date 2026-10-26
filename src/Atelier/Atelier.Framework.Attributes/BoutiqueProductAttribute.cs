namespace Atelier.Framework.Attributes;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public class BoutiqueProductAttribute : Attribute
{
    public Type ProductType { get; }
    public bool AutoStart { get; set; } = true;

    public BoutiqueProductAttribute(Type productType)
    {
        ProductType = productType;
    }
}
