namespace Atelier.Framework.Infrastructure.Generators.Compositors.Gateway.StrategyCalls;

public sealed class ValidateScopeCase : StrategyCall
{
    public required string ScopeParam { get; init; }
    public required string CancellationParam { get; init; }
}
