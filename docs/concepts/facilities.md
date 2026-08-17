# Infrastructure, Attache, and Gateways

In Atelier, infrastructure capabilities (such as database connections or Redis connections) are wired directly to the offerings that need them using their direct interfaces, avoiding intermediate framework wrappers. Cross-domain boundary communication is brokered by **Attache** at the host level and routed through source-generated **Gateways**.

---

## Direct Infrastructure Wiring

Rather than routing operations through generic, framework-defined layers (such as a generic `ICache` or `IDatabase` wrapper):
*   **Direct Interfaces**: Offerings declare dependencies directly on the client/driver interfaces they require (e.g. `IRedisConnectionProvider`, `IDbConnection`).
*   **Compile-Time Injection**: The `[Requisite]` generator matches these dependencies at compile time and registers them in the dependency injection container.
*   **Zero Redirect Overhead**: Infrastructure providers expose native concrete capabilities directly, matching the physical topology of the dedicated boutique.

---

## Attache

`IAttache` is the runtime broker for each boutique (unit of deployment). It manages capability requests, registers endpoints, and reports boutique health:

```csharp
public interface IAttache
{
    public Task<Outcome<CapabilityGrant>> RequestCapabilityAsync(
        CapabilityRequest request,
        CancellationToken cancellationToken = default);

    public Task<Outcome> ReleaseCapabilityAsync(
        string ticketId,
        CancellationToken cancellationToken = default);
        
    public Task<Outcome<HealthReport>> GetHealthReportAsync(
        CancellationToken cancellationToken = default);
}
```

The Attache manages resources, tracks slots using compare-and-swap (lock-free concurrency), and exposes the host's runtime health check telemetry.

---

## Gateways

A **Gateway** is a bridge that crosses a domain/network zone boundary. Mark an interface with `[DomainGateway]`, defining the source and target domains:

```csharp
[DomainGateway("Ordering", "Billing")]
public interface IBillingGateway
{
    // The GatewaySourceGenerator will emit the HTTP/gRPC transport bridge code 
    // to serialize and route calls across the network boundaries.
}
```

The `GatewaySourceGenerator` automatically emits all transport and serialization wiring based on the network zone topology configuration.

### Validation Rules
*   **ATELIER0701**: Every gateway must specify both a valid source domain and target domain.
*   **Boundary Enforcement**: Gateways sit at the boundary of a zone. All inbound and outbound traffic crossing a network zone boundary must traverse a designated gateway.
