namespace Atelier.Framework.Infrastructure.Generators.Compositors.Contract.Validations;

public sealed class CreditCardValidation : PropertyValidation
{
    public required string PropertyName { get; init; }
}
