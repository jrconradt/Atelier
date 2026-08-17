using Templar.Rendering;

namespace Atelier.Framework.Requisitions.Generators.Compositors.Injection.Assignments;

internal sealed class PlainAssignment : Compositor
{
    public required string MemberName { get; init; }
    public required string ParamName { get; init; }
}
