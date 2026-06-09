namespace Atelier.Framework.Infrastructure.Generators.Compositors.Contract.Validations;

public sealed class RangeValidation : PropertyValidation
{
    public required string PropertyName { get; init; }
    public required string Min { get; init; }
    public required string Max { get; init; }
}
