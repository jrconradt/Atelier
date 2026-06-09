namespace Atelier.Framework.Infrastructure.Generators.Compositors.Contract.Validations;

public sealed class StringLengthRange : PropertyValidation
{
    public required string PropertyName { get; init; }
    public required string MinLength { get; init; }
    public required string MaxLength { get; init; }
}
