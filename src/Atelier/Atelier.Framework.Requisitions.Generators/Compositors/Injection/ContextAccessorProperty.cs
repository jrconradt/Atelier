using Templar.Rendering;

namespace Atelier.Framework.Requisitions.Generators.Compositors.Injection;

internal sealed class ContextAccessorProperty : Compositor
{
    public required string SourceMember { get; init; }
}
