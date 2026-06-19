namespace Atelier.Framework.Attributes;

public enum EffectKind
{
    Read,
    Write
}

[AttributeUsage(
    AttributeTargets.Method,
    AllowMultiple = false,
    Inherited = true)]
public sealed class OperationEffectAttribute : Attribute
{
    public EffectKind Effect { get; }

    public OperationEffectAttribute(EffectKind effect)
    {
        Effect = effect;
    }
}
