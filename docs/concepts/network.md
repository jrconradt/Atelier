# Network

A service reaches another service only through a declared `[Requisite]`
dependency. That declaration is the whole of service-to-service access: there
is no separate network topology to configure.

Generating network infrastructure (Kubernetes `NetworkPolicy`, isolated
networks, per-service DNS) from those declarations is designed but not built —
see [Tiers × verticals](../design/tiers-and-verticals.md). The host generator
does not currently emit network policy.

## Compile-time diagnostics

`Atelier.Framework.Analyzers` raises the network diagnostic at build time:

| ID | Severity | Meaning |
|---|---|---|
| `ATELIER0330` | Warning | A service holds a handle to another service that is not a declared `[Requisite]`. |

## Runtime network surface

What runs at runtime is transport and authorization, both in
`Atelier.Framework.Network`.

**Transport.** `ITransportClient` and `ITransportServer` define the wire
surface, with `Http` and `InProcess` implementations and a JSON payload codec
(`ITransportPayloadCodec`). `TransportTlsOptions` configures certificates,
mutual TLS (`RequiresMutualTls`), client-certificate validation, and a TLS floor
of 1.2/1.3. A transport server handler returns `Task<Outcome>`.

**Scope authorization.** `ScopeRequirement` carries the required scopes and a
fail-closed flag; `ScopeAuthorizationEvaluator.IsAuthorized` checks a verified
`AuthorizationContext` against them, and `IsSelf` matches the caller's identity.
`ScopeEnforcementMiddleware` applies the requirement on the request path.

**Host discovery.** `NetworkHost` and `NetworkHostDiscovery` (`IHostDiscovery`)
describe and locate hosts, their ports, and their dependencies.

## See also

- [Requisites](requisites.md) — declaring dependencies.
- [Facilities](facilities.md) — facility access and authorization.
- [Diagnostics](../reference/diagnostics.md) — `ATELIER0330`.
