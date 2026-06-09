namespace Atelier.Framework.Infrastructure.Generators.Compositors.Contract.Validations;

public sealed class EnumValidation : PropertyValidation
{
    public required string PropertyName { get; init; }
    public required string EnumTypeName { get; init; }
}
