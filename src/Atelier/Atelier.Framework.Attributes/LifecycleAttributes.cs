namespace Atelier.Framework.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public class TransientAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public class ScopedAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public class SingletonAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public class PooledAttribute : Attribute
{
    public int MaxSize { get; set; } = 100;
    public int InitialSize { get; set; } = 10;
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public class ValueObjectAttribute : Attribute { }
