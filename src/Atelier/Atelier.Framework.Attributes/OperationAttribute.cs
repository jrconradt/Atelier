namespace Atelier.Framework.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class OperationAttribute : Attribute
{
    public string? Name { get; set; }

    public bool LogErrors { get; set; } = true;

    public bool LogExecution { get; set; } = true;

    public bool ThrowOnError { get; set; } = false;

    public OperationAttribute()
    {
    }

    public OperationAttribute(string name)
    {
        Name = name;
    }
}
