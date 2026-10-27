# Facilities and Gateways

A **Facility** is a concrete infrastructure capability — a cache, a database, a message broker — exposed to offerings through a flat, `Outcome`-typed contract. A **Gateway** is the source-generated bridge that lets one domain call another.

## The facility contract

A facility surface is an interface marked with `[Facility]`. The attribute declares the capability name and its access policy:

```csharp
[Facility("Cache",
          RequiresAuthentication = true,
          AllowAnonymous = false,
          RequiredScopes = new[] { "cache.access" })]
public interface ICache
{
    public Task<Outcome<CacheLookup>> GetAsync(
        CacheKey key,
        CancellationToken cancellationToken = default);

    public Task<Outcome> SetAsync(
        CacheKey key,
        CacheValue value,
        CancellationToken cancellationToken = default);

    public Task<Outcome> RemoveAsync(
        CacheKey key,
        CancellationToken cancellationToken = default);
}
```

Every method returns `Outcome` / `Outcome<T>` and takes a `CancellationToken`. Inputs and outputs are flat `[Contract]` DTOs (`CacheKey`, `CacheValue`, `CacheLookup`), never object graphs the consumer has to navigate.

## Providers

A facility provider derives from `FacilityBase`; it brokers resources and provisions the offering that implements the contract. `FacilityBase` supplies the resource-accounting surface: `FacilityId`, `FacilityName`, `Type` (`InProcess`, `OutOfProcess`, `NetworkMapped`, `Hybrid`), `CanFulfill`, `CheckResourceAvailabilityAsync`, `ProvisionAsync`, and `ReleaseAsync`. Resource slots are tracked with a `ConcurrentDictionary` and lock-free compare-and-swap, so providers never reach for synchronization primitives.

`RedisCacheFacility : FacilityBase` is the worked example: it provisions and manages the `RedisCache : ICache` offering that implements the contract against Redis.

## Cache operational contract

The `RedisCache` provider connects to a single Redis endpoint and has no local or second-level fallback tier. A Redis outage therefore surfaces as a failed `Outcome` on every `GetAsync` / `SetAsync` / `RemoveAsync` rather than degrading to a cache miss. Availability is an operational requirement, not a provider responsibility: deploy Redis in a highly available topology (Sentinel or Cluster) behind a single advertised endpoint. Callers must treat a failed cache `Outcome` as a hard error and decide whether to fall through to their source of truth; the cache layer does not silently swallow connection failures.

Cache values are stored in Redis as plaintext. Confidentiality at rest depends entirely on Redis and host configuration; the `ICache` layer applies no application-level encryption. Callers must not place PII, secrets, or other sensitive data in the cache. The cache is a transient performance tier, not a system of record, and carries no confidentiality guarantee for its contents.

## Gateways

A **Gateway** crosses a domain boundary. Mark an interface with `[DomainGateway]`, naming the source and target domains:

```csharp
[DomainGateway("Ordering", "Billing")]
public interface IBillingGateway
{
}
```

`GatewaySourceGenerator` emits the bridge wiring. Both domains are required: `ATELIER0701` is an error when a gateway does not specify source and target domains.

## See also

- [Outcomes](outcomes.md) — every facility method is `Outcome`-typed.
- [Diagnostics](../reference/diagnostics.md) — `ATELIER0700` / `ATELIER0701` / `ATELIER0720`.
