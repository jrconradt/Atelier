using Templar.Rendering;

namespace Atelier.Framework.Requisitions.Generators.Compositors.Factory.FactoryConstructors;

internal abstract class FactoryConstructor : Compositor
{
    public required string TypeName { get; init; }
    public required Sequence Assignments { get; init; }
}
