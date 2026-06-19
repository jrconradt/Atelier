using Templar.Rendering;

namespace Atelier.Framework.Requisitions.Generators.Compositors.Injection.Assignments;

internal sealed class PropertyNullCheckedAssignment : Compositor
{
    public required string MemberName { get; init; }
    public required string ParamName { get; init; }
}
