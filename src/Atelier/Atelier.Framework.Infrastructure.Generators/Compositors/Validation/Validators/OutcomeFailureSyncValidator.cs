namespace Atelier.Framework.Infrastructure.Generators.Compositors.Validation.Validators;

public sealed class OutcomeFailureSyncValidator : ValidationStatement
{
    public required string OutcomeExpression { get; init; }
}
