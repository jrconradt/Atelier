using Templar.Rendering;

namespace Atelier.Framework.Requisitions.Generators.Compositors.Helper.HelperBodies;

internal sealed class ValueObjectBody : HelperBody
{
    public required string TypeName { get; init; }
    public required Sequence ParamExtractions { get; init; }
    public required Sequence ParamValues { get; init; }
}
