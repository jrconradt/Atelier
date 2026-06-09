namespace Atelier.Framework.Infrastructure.Generators.Compositors.Contract.Validations;

public sealed class MaxLengthValidation : PropertyValidation
{
    public required string PropertyName { get; init; }
    public required string LengthProperty { get; init; }
    public required string MaxLength { get; init; }
}
