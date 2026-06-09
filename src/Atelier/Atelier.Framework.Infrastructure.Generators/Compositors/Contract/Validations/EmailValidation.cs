namespace Atelier.Framework.Infrastructure.Generators.Compositors.Contract.Validations;

public sealed class EmailValidation : PropertyValidation
{
    public required string PropertyName { get; init; }
}
