using Atelier.Framework.Primitives;

namespace Atelier.Framework.Attributes;

[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public class InfrastructureAttribute : Attribute
{
    public Type? ServiceType { get; }
    public Type? ImplementationType { get; }
    public InfrastructureLifetime Lifetime { get; }

    public InfrastructureAttribute(InfrastructureLifetime lifetime = InfrastructureLifetime.Singleton)
    {
        ServiceType = null;
        ImplementationType = null;
        Lifetime = lifetime;
    }

    public InfrastructureAttribute(
        Type serviceType,
        Type implementationType,
        InfrastructureLifetime lifetime = InfrastructureLifetime.Singleton)
    {
        ServiceType = serviceType;
        ImplementationType = implementationType;
        Lifetime = lifetime;
    }
}

