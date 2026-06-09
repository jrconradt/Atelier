namespace Atelier.Framework.Infrastructure.Generators.Compositors.Contract.Validations;

public sealed class RequiredString : PropertyValidation
{
    public required string PropertyName { get; init; }
}
