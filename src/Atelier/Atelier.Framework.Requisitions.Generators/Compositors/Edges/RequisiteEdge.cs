using Templar.Rendering;

namespace Atelier.Framework.Requisitions.Generators.Compositors.Edges;

internal sealed class RequisiteEdge : Compositor
{
    public required string Service { get; init; }
    public required string Requisite { get; init; }
}
