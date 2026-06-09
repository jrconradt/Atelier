using Templar.Rendering;

namespace Atelier.Framework.Infrastructure.Generators.Compositors.Gateway.StrategyCalls;

public sealed class CreateScopeCase : StrategyCall
{
    public required Sequence Arguments { get; init; }
}
