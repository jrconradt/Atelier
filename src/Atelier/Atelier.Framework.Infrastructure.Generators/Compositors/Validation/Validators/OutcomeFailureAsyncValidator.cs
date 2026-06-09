namespace Atelier.Framework.Infrastructure.Generators.Compositors.Validation.Validators;

public sealed class OutcomeFailureAsyncValidator : ValidationStatement
{
    public required string TaskTypeName { get; init; }
    public required string OutcomeExpression { get; init; }
}
