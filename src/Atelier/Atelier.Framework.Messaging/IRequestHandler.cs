using Atelier.Framework.Outcomes;

namespace Atelier.Framework.Messaging;

/// <summary>
/// Marks every request handler, so the boutique can hand <see cref="HandlerFactory"/> the
/// handlers it declares instead of the factory resolving them by type.
/// </summary>
public interface IRequestHandler
{
}

public interface IRequestHandler<TRequest, TResponse> : IRequestHandler
    where TRequest : class
    where TResponse : class
{
    public Task<Outcome<TResponse>> HandleAsync(
        TRequest request,
        CancellationToken cancellationToken = default);
}

