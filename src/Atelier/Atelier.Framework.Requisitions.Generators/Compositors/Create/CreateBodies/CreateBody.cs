using Templar.Rendering;

namespace Atelier.Framework.Requisitions.Generators.Compositors.Create.CreateBodies;

internal abstract class CreateBody : Compositor
{
    public required string TypeName { get; init; }
}
