namespace Atelier.Framework.Infrastructure.Generators.Compositors.Contract.Validations;

public sealed class StringLengthMax : PropertyValidation
{
    public required string PropertyName { get; init; }
    public required string MaxLength { get; init; }
}
