using Atelier.Framework.Primitives;
using Atelier.Framework.Attributes;
using Atelier.Framework.Requisitions;

namespace Atelier.Bench.Requisitions;

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public sealed class PrimaryDependency
{
    public string Token => "primary";
}

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public sealed class SecondaryDependency
{
    public string Token => "secondary";
}

[Infrastructure(InfrastructureLifetime.Transient)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public sealed partial class WiredService
{
    [Requisite] private readonly PrimaryDependency _primary = null!;
    [Requisite(Required = false)] private readonly SecondaryDependency _secondary = null!;

    public PrimaryDependency Primary => _primary;
    public SecondaryDependency? Secondary => _secondary;

    public string Resolve()
    {
        return $"{_primary.Token}:{_secondary!.Token}";
    }
}
