using Templar.Rendering;

namespace Atelier.Framework.Requisitions.Generators.Compositors.Pool.ResetLines;

internal abstract class ResetLine : Compositor
{
    public required string Target { get; init; }
    public required string MemberName { get; init; }
}
