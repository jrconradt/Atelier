namespace Atelier.Framework.Attributes;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class RuntimeAttribute : Attribute
{
    public bool Required { get; set; } = true;
}
