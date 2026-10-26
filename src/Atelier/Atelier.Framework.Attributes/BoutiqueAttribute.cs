namespace Atelier.Framework.Attributes;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public class BoutiqueAttribute : Attribute
{
    public string Id { get; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string Version { get; set; } = "1.0.0";

    public BoutiqueAttribute(string id)
    {
        Id = id;
    }
}
