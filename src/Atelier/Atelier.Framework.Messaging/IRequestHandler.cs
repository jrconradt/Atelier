using Atelier.Framework.Outcomes;

namespace Atelier.Framework.Messaging;

public interface IRequestHandler<TRequest, TResponse>
    where TRequest : class
    where TResponse : class
{
    public Task<Outcome<TResponse>> HandleAsync(
        TRequest request,
        CancellationToken cancellationToken = default);
}

