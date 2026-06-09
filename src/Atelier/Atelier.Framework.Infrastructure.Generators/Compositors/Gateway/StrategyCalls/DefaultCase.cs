using Templar.Rendering;

namespace Atelier.Framework.Infrastructure.Generators.Compositors.Gateway.StrategyCalls;

public sealed class DefaultCase : StrategyCall
{
    public required string MethodName { get; init; }
    public required Sequence Arguments { get; init; }
}
