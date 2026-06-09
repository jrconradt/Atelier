namespace Atelier.Framework.Network.Generators.Compositors;

internal sealed class SingleParamPayload : PayloadStrategy
{
    public required string ParamName { get; init; }
}
