using Atelier.Framework.Primitives;
using Atelier.Framework.Attributes;
using Atelier.Framework.Requisitions;

namespace Atelier.Framework.Messaging;

[Infrastructure(InfrastructureLifetime.Scoped)]
public partial class HandlerFactory : IHandlerFactory
{
    [Requisite] protected readonly IEnumerable<IRequestHandler> _handlers = null!;

    public IRequestHandler<TRequest, TResponse>? GetHandler<TRequest, TResponse>()
        where TRequest : class
        where TResponse : class
    {
        return _handlers.OfType<IRequestHandler<TRequest, TResponse>>().FirstOrDefault();
    }

    public object? GetHandler(Type handlerType)
    {
        ArgumentNullException.ThrowIfNull(handlerType);
        return _handlers.FirstOrDefault(handlerType.IsInstanceOfType);
    }

    public bool HasHandler<TRequest, TResponse>()
        where TRequest : class
        where TResponse : class
    {
        return _handlers.OfType<IRequestHandler<TRequest, TResponse>>().Any();
    }
}
