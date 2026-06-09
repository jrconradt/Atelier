using Atelier.Framework.Context;
using Atelier.Framework.Outcomes;

namespace Atelier.Framework.Messaging;

public interface IHandlerRegistry
{
    public Task<Outcome<TResponse>> HandleAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class;
}

