namespace Atelier.Framework.Infrastructure.Generators.Compositors.Contract.Validations;

public sealed class RegexValidation : PropertyValidation
{
    public required string PropertyName { get; init; }
    public required string Pattern { get; init; }
}
