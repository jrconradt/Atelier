using Templar.Rendering;

namespace Atelier.Framework.Requisitions.Generators.Compositors.Injection;

internal sealed class BaseArgument : Compositor
{
    public required string ParamName { get; init; }
}
