namespace Atelier.Framework.Attributes;

[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Interface,
    AllowMultiple = false,
    Inherited = true)]
public sealed class ScopeResourceAttribute : Attribute
{
    public Type ScopePairType { get; }

    public ScopeResourceAttribute(Type scopePairType)
    {
        ScopePairType = scopePairType ?? throw new ArgumentNullException(nameof(scopePairType));
    }
}
