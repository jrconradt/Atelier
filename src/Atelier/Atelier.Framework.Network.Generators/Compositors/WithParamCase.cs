using Templar.Rendering;

namespace Atelier.Framework.Network.Generators.Compositors;

internal sealed class WithParamCase : ServerCase
{
    public required string MethodName { get; init; }
    public required string ParamType { get; init; }
    public required ReturnHandling ReturnHandling { get; init; }
    public required AuthorizationGuard AuthorizationGuard { get; init; }
}
