using Atelier.Framework.Context;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Testing;

namespace Atelier.Framework.Messaging;

public sealed class IntegrationPingRequest
{
    public required string Token { get; init; }
}

public sealed class IntegrationPongResponse
{
    public required string Echo { get; init; }
}

public sealed class IntegrationPingHandler : IRequestHandler<IntegrationPingRequest, IntegrationPongResponse>
{
    public Task<Outcome<IntegrationPongResponse>> HandleAsync(
        IntegrationPingRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(request.Token))
        {
            return Task.FromResult(Outcome<IntegrationPongResponse>.Failure());
        }
        return Task.FromResult(Outcome<IntegrationPongResponse>.Success(
            new IntegrationPongResponse { Echo = $"pong:{request.Token}" }));
    }
}

public sealed class IntegrationThrowingHandler : IRequestHandler<IntegrationPingRequest, IntegrationPongResponse>
{
    public const string FAILURE_MESSAGE = "handler blew up";

    public Task<Outcome<IntegrationPongResponse>> HandleAsync(
        IntegrationPingRequest request,
        CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException(FAILURE_MESSAGE);
    }
}

public sealed class SingleHandlerFactory : IHandlerFactory
{
    private readonly object _handler;

    public SingleHandlerFactory(object handler)
    {
        _handler = handler;
    }

    public IRequestHandler<TRequest, TResponse>? GetHandler<TRequest, TResponse>()
        where TRequest : class
        where TResponse : class
    {
        return _handler as IRequestHandler<TRequest, TResponse>;
    }

    public object? GetHandler(Type handlerType)
    {
        return handlerType.IsInstanceOfType(_handler) ? _handler : null;
    }

    public bool HasHandler<TRequest, TResponse>()
        where TRequest : class
        where TResponse : class
    {
        return _handler is IRequestHandler<TRequest, TResponse>;
    }
}

public static class HandlerRegistryDispatchIntegrationTests
{
    private const string TARGET = "global::Atelier.Framework.Messaging.HandlerRegistry";

    [GeneratedTest("messaging.integration.real-handler-dispatch-success", TARGET)]
    public static async Task RealHandlerDispatchedThroughRealRegistryPropagatesSuccess()
    {
        var factory = new SingleHandlerFactory(new IntegrationPingHandler());
        var registry = new HandlerRegistry(factory,
                                           null);

        var outcome = await registry.HandleAsync<IntegrationPingRequest, IntegrationPongResponse>(
            new IntegrationPingRequest { Token = "abc" });

        if (!outcome.IsSuccess)
        {
            throw new InvalidOperationException(
                "Expected success from real handler dispatch, got failure");
        }
        if (outcome.Data is null)
        {
            throw new InvalidOperationException("Successful dispatch returned null Data");
        }
        if (outcome.Data.Echo != "pong:abc")
        {
            throw new InvalidOperationException(
                $"Handler response did not propagate through the registry; expected 'pong:abc', got '{outcome.Data.Echo}'");
        }
    }

    [GeneratedTest("messaging.integration.real-handler-dispatch-failure", TARGET)]
    public static async Task RealHandlerFailureOutcomePropagatesThroughRealRegistry()
    {
        var factory = new SingleHandlerFactory(new IntegrationPingHandler());
        var registry = new HandlerRegistry(factory,
                                           null);

        var outcome = await registry.HandleAsync<IntegrationPingRequest, IntegrationPongResponse>(
            new IntegrationPingRequest { Token = string.Empty });

        if (outcome.IsSuccess)
        {
            throw new InvalidOperationException("Expected the handler's failure Outcome to propagate, got success");
        }
        if (outcome.Data is not null)
        {
            throw new InvalidOperationException("Failure Outcome should carry no Data");
        }
    }

    [GeneratedTest("messaging.integration.unregistered-handler-failure", TARGET)]
    public static async Task MissingHandlerYieldsHandlerNotFoundThroughRealRegistry()
    {
        var factory = new SingleHandlerFactory(new IntegrationPingHandler());
        var registry = new HandlerRegistry(factory,
                                           null);

        var outcome = await registry.HandleAsync<IntegrationPongResponse, IntegrationPingRequest>(
            new IntegrationPongResponse { Echo = "unmatched" });

        if (outcome.IsSuccess)
        {
            throw new InvalidOperationException("Expected failure for an unregistered request type, got success");
        }
        if (outcome.Data is not null)
        {
            throw new InvalidOperationException("Failure for an unregistered handler should carry no Data");
        }
    }

    [GeneratedTest("messaging.integration.handler-throws-maps-to-failure", TARGET)]
    public static async Task HandlerThatThrowsIsCaughtAndSurfacedAsFailureOutcome()
    {
        var factory = new SingleHandlerFactory(new IntegrationThrowingHandler());
        var registry = new HandlerRegistry(factory,
                                           null);

        var outcome = await registry.HandleAsync<IntegrationPingRequest, IntegrationPongResponse>(
            new IntegrationPingRequest { Token = "boom" });

        if (outcome.IsSuccess)
        {
            throw new InvalidOperationException("Expected a thrown handler exception to be caught and surfaced as a failure Outcome, got success");
        }
        if (outcome.Data is not null)
        {
            throw new InvalidOperationException("Failure from a thrown handler exception should carry no Data");
        }
    }
}
