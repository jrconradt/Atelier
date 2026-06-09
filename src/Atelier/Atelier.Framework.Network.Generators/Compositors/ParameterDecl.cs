using Templar.Rendering;

namespace Atelier.Framework.Network.Generators.Compositors;

internal sealed class ParameterDecl : Compositor
{
    public required string Type { get; init; }
    public required string Name { get; init; }
}
