using System.Reflection;

namespace Atelier.Framework.Network.Middleware;

public sealed class ScopeEnforcedOperation
{
    public MethodInfo Operation { get; }

    public ScopeEnforcedOperation(MethodInfo operation)
    {
        Operation = operation ?? throw new ArgumentNullException(nameof(operation));
    }
}
