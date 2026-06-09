using Templar.Rendering;

namespace Atelier.Framework.Requisitions.Generators.Compositors.Injection.Assignments;

internal abstract class Assignment : Compositor
{
    public required string DeclaringTypeName { get; init; }
    public required string MemberName { get; init; }
    public required string ParamName { get; init; }
}
