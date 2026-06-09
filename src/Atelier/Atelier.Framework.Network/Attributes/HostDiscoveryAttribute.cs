using Atelier.Framework.Primitives;
using Atelier.Framework.Attributes;

namespace Atelier.Framework.Network.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class ServiceDiscoveryAttribute : Attribute
{
    public InfrastructureLifetime Lifetime { get; }

    public ServiceDiscoveryAttribute(InfrastructureLifetime lifetime)
    {
        Lifetime = lifetime;
    }
}
