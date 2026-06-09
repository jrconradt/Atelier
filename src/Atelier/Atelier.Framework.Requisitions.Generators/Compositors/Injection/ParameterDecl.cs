using Templar.Rendering;

namespace Atelier.Framework.Requisitions.Generators.Compositors.Injection;

internal sealed class ParameterDecl : Compositor
{
    public required string ParamType { get; init; }
    public required string ParamName { get; init; }
    public string DefaultClause { get; init; } = string.Empty;
}
