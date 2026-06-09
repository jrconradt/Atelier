using Microsoft.CodeAnalysis;

namespace Atelier.Framework.Generators.Requisition;

internal class FactoryTypeInfo
{
    public INamedTypeSymbol TypeSymbol { get; set; } = null!;
    public LifecycleType Lifecycle { get; set; }
    public bool IsPooled { get; set; }
    public int MaxPoolSize { get; set; }
    public int InitialPoolSize { get; set; }
}

internal enum LifecycleType
{
    Transient,
    Scoped,
    Singleton
}
