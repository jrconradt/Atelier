namespace Atelier.Framework.Context;

public readonly struct EntityContextScope : IDisposable
{
    private readonly IContextAccessor? _accessor;
    private readonly IContext? _previous;

    internal EntityContextScope(
        IContextAccessor? accessor,
        IContext? previous)
    {
        _accessor = accessor;
        _previous = previous;
    }

    public void Dispose()
    {
        if (_accessor is not null
            && _previous is not null)
        {
            _accessor.SetCurrent(_previous);
        }
    }
}

public static class EntityContext
{
    public static EntityContextScope Enter(
        IContextAccessor? accessor,
        string kind,
        string id)
    {
        if (accessor is null)
        {
            return new EntityContextScope(null, null);
        }

        var previous = accessor.Current;
        var entity = new CompositeContext(
            previous is not null ? $"{previous.ContextId}.{id}" : id,
            $"{kind}-{id}",
            previous);
        if (previous is Context parentContext)
        {
            parentContext.PropagateInheritableState(entity);
        }
        accessor.SetCurrent(entity);
        return new EntityContextScope(accessor, previous);
    }
}
