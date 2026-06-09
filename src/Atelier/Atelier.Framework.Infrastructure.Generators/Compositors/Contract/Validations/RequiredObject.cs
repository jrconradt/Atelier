namespace Atelier.Framework.Infrastructure.Generators.Compositors.Contract.Validations;

public sealed class RequiredObject : PropertyValidation
{
    public required string PropertyName { get; init; }
}
