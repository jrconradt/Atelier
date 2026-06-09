namespace Atelier.Framework.Infrastructure.Generators.Compositors.Contract.Validations;

public sealed class MinLengthValidation : PropertyValidation
{
    public required string PropertyName { get; init; }
    public required string LengthProperty { get; init; }
    public required string MinLength { get; init; }
}
