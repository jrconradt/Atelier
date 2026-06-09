namespace Atelier.Framework.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class GrpcAttribute : Attribute
{
    public string[]? Claims { get; set; }

    public GrpcAttribute(string[]? claims = null)
    {
        Claims = claims;
    }
}
