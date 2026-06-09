# Network zones and policy

A service's network position is declarative. Every service is assigned to a
**zone** — a marker type — and each zone carries a `[ZonePolicy]` describing
which zones may reach it, which zones it may reach, whether mutual TLS is
required, and whether it is isolated. Zones are not enforced at runtime: the
`smash` build tool reads the declarations and compiles them to Kubernetes
`NetworkPolicy` infrastructure-as-code. The runtime network concern is the
transport layer and scope authorization, covered below.

## Zones

Zones are marker classes in `Atelier.Framework.Primitives`, each implementing
the empty `INetworkZone` interface:

| Zone | Typical occupant |
|---|---|
| `Application` | Business offerings. |
| `Internal` | Internal-only services. |
| `External` | Edge / public-facing services. |
| `Web` | Web front ends. |
| `Security` | Authentication, authorization, secrets. |
| `Data` | Databases, backups. |
| `Management` | Routing, monitoring, disaster recovery. |

## Zone policy

Each zone marker carries a `[ZonePolicy]` (`Atelier.Framework.Primitives`). The
policy lists the inbound and outbound zones as marker types, whether the zone
requires mutual TLS, and whether it is isolated:

```csharp
[ZonePolicy(
    AllowedInbound = new[] { typeof(Application), typeof(Management) },
    AllowedOutbound = new Type[] { },
    RequiresMutualTls = true,
    Isolates = true)]
public sealed class Security : INetworkZone
{
}
```

`AllowedInbound` and `AllowedOutbound` default to empty, `RequiresMutualTls` and
`Isolates` to `false`. A zone with `Isolates = true` (such as `Security` and
`Data`) is given its own isolated network rather than the default bridge.

## Assigning a zone

Annotate a service with `[NetworkZone(typeof(Zone))]`
(`Atelier.Framework.Attributes`). The attribute carries only the zone marker;
the inbound/outbound/TLS rules come from that marker's `[ZonePolicy]`.

```csharp
[NetworkZone(typeof(Security))]
[Infrastructure(InfrastructureLifetime.Singleton)]
public partial class RequisitionAuthorizer : IAtelier, IRequisitionAuthorizer
{
}
```

## Compiling to infrastructure

The `smash` build tool's generation pass (`smash --generate-boutiques`) fans
zone declarations out to Kubernetes infrastructure. `ProductDependencyAnalyzer`
walks each service's `[NetworkZone]`, follows the zone marker to its
`[ZonePolicy]`, and records a `ZonePolicyInfo`. `NetworkPolicyGenerator` renders
one Kubernetes `NetworkPolicy` per zone and writes `network-policies.yaml` at
the solution root:

```yaml
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: security-zone-policy
  labels:
    io.atelier.zone: "security"
  annotations:
    io.atelier.requires-mtls: "true"
spec:
  podSelector:
    matchLabels:
      io.atelier.zone: "security"
  policyTypes:
    - Ingress
    - Egress
  ingress:
    - from:
        - podSelector:
            matchLabels:
              io.atelier.zone: "application"
        - podSelector:
            matchLabels:
              io.atelier.zone: "management"
  egress:
    []
```

The same pass emits the isolated zones (`Isolates = true`) as dedicated networks
in the generated `docker-compose.yml`. The rendering surface lives in
`Atelier.Framework.Network`: `Templates/Network` holds the Kubernetes
`NetworkPolicy`, service-mesh `ConfigMap`, docker-compose-network, and mermaid
topology templates, and `Compositors/Network/Variants` holds the Istio
`PeerAuthentication` mesh policy (strict and open) and mutual-TLS docker network
variants. These artifacts are generated — regenerate via `smash`, do not
hand-edit them.

## Compile-time diagnostics

`Atelier.Framework.Analyzers` raises the network diagnostics at build time:

| ID | Severity | Meaning |
|---|---|---|
| `ATELIER0300` | Warning | Service is missing a `[NetworkZone]`. |
| `ATELIER0310` | Error | A declared dependency crosses a zone boundary the policy forbids. |
| `ATELIER0320` | Warning | Service communication is unencrypted. |
| `ATELIER0330` | Warning | A service depends on another without declaring the dependency. |

## Runtime network surface

Zones are build-time infrastructure; what runs at runtime is transport and
authorization, both in `Atelier.Framework.Network`.

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

- [Offerings](offerings.md) — offerings carry `[NetworkZone]`.
- [Facilities](facilities.md) — facility access and authorization.
- [Diagnostics](../reference/diagnostics.md) — `ATELIER0300`–`ATELIER0330`.
