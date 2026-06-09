using Templar.Rendering;

namespace Atelier.Framework.Requisitions.Generators.Compositors.Injection;

internal sealed class InjectionFile : Compositor
{
    public required string ClassName { get; init; }
    public required string TypeParameters { get; init; }
    public required Sequence Sections { get; init; }
}
