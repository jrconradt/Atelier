using Templar.Rendering;

namespace Atelier.Framework.Requisitions.Generators.Compositors.Helper.HelperBodies;

internal sealed class NonPooledBody : HelperBody
{
    public required string TypeName { get; init; }
    public required Sequence MemberMappings { get; init; }
}
