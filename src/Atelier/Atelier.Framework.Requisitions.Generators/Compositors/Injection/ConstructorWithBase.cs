using Templar.Rendering;

namespace Atelier.Framework.Requisitions.Generators.Compositors.Injection;

internal sealed class ConstructorWithBase : Compositor
{
    public required string ClassName { get; init; }
    public required Sequence Parameters { get; init; }
    public required Sequence BaseArguments { get; init; }
    public required Sequence Assignments { get; init; }
}
