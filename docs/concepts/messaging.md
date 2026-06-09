# Messaging

Messaging is request/response dispatch. A request type maps to a single handler that returns an `Outcome<TResponse>`; the `IHandlerRegistry` routes a request to its handler.

## Handlers

A handler implements `IRequestHandler<TRequest, TResponse>`:

```csharp
public sealed class GetUserHandler : IRequestHandler<GetUserRequest, UserDto>
{
    public Task<Outcome<UserDto>> HandleAsync(
        GetUserRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Outcome<UserDto>.Success(new UserDto()));
    }
}
```

Both `TRequest` and `TResponse` are reference types, and the result is always `Outcome<TResponse>` — failures flow as `Outcome.Failure(...)` rather than thrown exceptions.

## Dispatch

Callers go through `IHandlerRegistry`, which resolves the registered handler for the request/response pair and invokes it:

```csharp
public Task<Outcome<TResponse>> HandleAsync<TRequest, TResponse>(
    TRequest request,
    CancellationToken cancellationToken = default)
    where TRequest : class
    where TResponse : class;
```

`HandlerRegistry` and `HandlerFactory` resolve the concrete `IRequestHandler<,>` and construct it with its requisites filled.

## Envelopes and context

A `MessageEnvelope<TPayload>` wraps a payload with its `MessageHeaders` and `MessageRoutingInfo`, which carries the `DeliveryGuarantee`. `MessagingContextExtension` / `ContextMessagingExtensions` propagate ambient execution context across a message boundary, and `MessageEnvelopeSerializer` / `MessagingContextSerializer` carry it over the wire.

## See also

- [Outcomes](outcomes.md) — handlers return `Outcome<TResponse>`.
- [Requisites](requisites.md) — handlers receive dependencies as `[Requisite]` fields.
