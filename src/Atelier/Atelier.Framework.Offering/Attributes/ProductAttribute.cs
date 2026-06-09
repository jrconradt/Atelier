
namespace Atelier.Framework.Offering.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ProductAttribute : Attribute
{
        public string? Name { get; set; }

        public string? Version { get; set; }

        public string? Description { get; set; }
}
