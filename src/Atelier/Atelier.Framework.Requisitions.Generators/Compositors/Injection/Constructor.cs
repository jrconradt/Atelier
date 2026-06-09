using Templar.Rendering;

namespace Atelier.Framework.Requisitions.Generators.Compositors.Injection;

internal sealed class Constructor : Compositor
{
    public required string ClassName { get; init; }
    public required Sequence Parameters { get; init; }
    public required Sequence Assignments { get; init; }
}
