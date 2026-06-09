namespace Atelier.Framework.Infrastructure.Generators.Compositors.Operation.FailureModes;

public sealed class OutcomeFailureWithType : FailureMode
{
    public required string TypeArg { get; init; }
}
