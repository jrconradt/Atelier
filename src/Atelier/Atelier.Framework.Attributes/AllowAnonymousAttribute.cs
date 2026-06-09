
namespace Atelier.Framework.Attributes;

[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Class,
    AllowMultiple = false,
    Inherited = true)]
public class AllowAnonymousAttribute : Attribute
{
}
