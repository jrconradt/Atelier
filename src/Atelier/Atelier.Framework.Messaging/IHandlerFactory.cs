using Atelier.Framework.Outcomes;

namespace Atelier.Framework.Messaging;

public interface IHandlerFactory
{
        public IRequestHandler<TRequest, TResponse>? GetHandler<TRequest, TResponse>()
        where TRequest : class
        where TResponse : class;

        public object? GetHandler(Type handlerType);

        public bool HasHandler<TRequest, TResponse>()
        where TRequest : class
        where TResponse : class;
}
