namespace Atelier.Framework.Infrastructure.Generators.Compositors.Contract.Validations;

public sealed class NullCheck : PropertyValidation
{
    public required string PropertyName { get; init; }
}
