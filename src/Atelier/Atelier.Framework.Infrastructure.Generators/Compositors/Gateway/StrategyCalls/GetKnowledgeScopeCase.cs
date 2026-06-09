namespace Atelier.Framework.Infrastructure.Generators.Compositors.Gateway.StrategyCalls;

public sealed class GetKnowledgeScopeCase : StrategyCall
{
    public required string SessionParam { get; init; }
    public required string CancellationParam { get; init; }
}
