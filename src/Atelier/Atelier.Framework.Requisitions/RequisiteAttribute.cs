namespace Atelier.Framework.Requisitions;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class RequisiteAttribute : Attribute
{
    public bool Required { get; set; } = true;
}
