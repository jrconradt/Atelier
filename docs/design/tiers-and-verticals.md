# Design: tiers × verticals

**Status:** proposed — not implemented. Nothing in this note describes current
behavior unless it says so. The note separates what is **decided** from what is
only **proposed**; proposals are candidates, not commitments.

## Decided

### Principle: the topology is closed at compile time

Everything a deployment's network and service-auth posture depends on is known
when the code is built: scopes, claims, the number of applications, and each
application's required access. Every new thing necessitates a build, and every
new thing necessitates a deployment.

Making this a generated compile-time concern affirms: *the auth associated with
this service is known at compile time, and will not change until another build.*

### Tiers

Horizontal and ordered, outermost first:

| Tier | Holds |
|---|---|
| Public | Public front-end services; public user authentication. |
| Ingress (DMZ) | Public APIs for authentication and public service access — the gateways, divided by subnets or similar. |
| Internal | Service gateway and core application services, segmented by service. |
| Data | Third-party gateway and data-access gateway, segmented by source. |

A service's tier comes from the role markers that already describe it —
`[Api]`, Offering, Infrastructure, Gateway, and so on — not from a dedicated
tier marker. Roles without a marker today get one when there is a need.

### Verticals

The segmentation that divides the Ingress gateways propagates downward. Each
segmentation layer creates a clearly delineated vertical: a request for a
resource falls straight through its vertical, and a request into another layer's
segment gets nothing.

Verticals are derived from the `[Requisite]` graph: a vertical is what is
reachable downward from an Ingress gateway along declared dependencies. They are
not declared separately.

```
            ║  auth             ║  orders             ║  catalog            ║  payments
 ═══════════╬═══════════════════╬═════════════════════╬═════════════════════╬═════════════════════
  PUBLIC    ║  login UI /       ║   storefront FE     ║   storefront FE     ║
            ║  public authN     ║         │           ║         │           ║
 ───────────╫─────────│─────────╫─────────│───────────╫─────────│───────────╫──────────────────────
  INGRESS   ║  auth API gw      ║   orders API gw     ║   catalog API gw    ║
  (DMZ)     ║     [segment A]   ║      [segment B]    ║      [segment C]    ║
 ───────────╫─────────│─────────╫─────────│───────────╫─────────│───────────╫──────────────────────
  INTERNAL  ║  identity svc     ║  service gw         ║  service gw         ║  service gw
            ║                   ║    └► orders core   ║    └► catalog core  ║    └► payments core
 ───────────╫─────────│─────────╫─────────│───────────╫─────────│───────────╫─────────│────────────
  DATA      ║  data-access gw   ║  data-access gw     ║  data-access gw     ║  third-party gw
            ║   └► user store   ║   └► orders DB      ║   └► catalog DB     ║   └► payment provider
 ═══════════╩═══════════════════╩═════════════════════╩═════════════════════╩═════════════════════

   straight down within a vertical: reachable
   into another vertical's segment: nothing
```

The vertical names and services in the diagram are illustrative.

### Service identity

Service identity is generated from this model, alongside the network policy.

## Proposed, not decided

### Role → tier mapping

A candidate reading of the existing markers:

| Tier | Roles |
|---|---|
| Public | Front ends — no marker yet. |
| Ingress | `[Api]` classes. |
| Internal | Offerings and `[Infrastructure]` services; `[DomainGateway]` as the service gateway. |
| Data | `[Facility]` contracts as data-access gateways; third-party gateway — no marker yet. |

### Compile-time enforcement

Plausible, undecided: build errors for a dependency that is not exactly one tier
down, and for a service whose tier cannot be inferred.

### Layouts

Worked through one vertical: `orders-api` (`[Api]`, Ingress) requires
`orders-core` (Offering, Internal), which requires `orders-store` (`[Facility]`,
Data), which reaches the `orders-db` database. That is three edges. The layouts
render edges, not zones, so a shared lower node (open question 1) needs no
special case: it simply has more than one inbound edge.

**Kubernetes.**

- One `ServiceAccount` per service, and every pod labelled
  `io.atelier.service`, `io.atelier.tier`, and `io.atelier.vertical`. Policy
  selects on `io.atelier.service`; tier and vertical labels are for humans and
  tooling.
- A default-deny policy for the namespace, ingress and egress, plus an egress
  allowance to cluster DNS (without it nothing resolves).
- One policy per edge, on the target, admitting exactly the source:

  ```yaml
  apiVersion: networking.k8s.io/v1
  kind: NetworkPolicy
  metadata:
    name: orders-core--from--orders-api
  spec:
    podSelector:
      matchLabels: { io.atelier.service: orders-core }
    policyTypes: [Ingress]
    ingress:
      - from:
          - podSelector:
              matchLabels: { io.atelier.service: orders-api }
  ```

  and the mirror egress rule on the source. Default-deny plus per-edge allows
  means anything not generated has no route.
- Entry: only Ingress-tier pods admit traffic from the cluster's ingress
  controller. Exit: only third-party gateways get egress outside the cluster,
  via `ipBlock` or a CNI that supports FQDN rules.

**Docker Compose.**

- No shared default network. One network per edge — `net-orders-api--orders-core`,
  `net-orders-core--orders-store`, `net-orders-store--orders-db` — and each
  container joins only the networks of its own edges. A container on two edges
  sits on two networks; neither neighbour can see the other.
- Networks are `internal: true` except the one carrying a third-party gateway's
  egress. Only Public and Ingress containers publish host ports.
- Each data source is its own container on its own edge network; there is no
  environment-wide Postgres or Redis.
- Network count equals edge count. The Docker daemon's default address pools
  give about 31 bridge networks, so anything larger needs
  `default-address-pools` with smaller subnets — or Compose kept to development
  topologies.

### Authentication

Two kinds of caller, both closed at compile time where it is possible.

**Service to service.** Each service's generated identity (its `ServiceAccount`
and mTLS certificate) is the only identity its inbound edges admit, and its
generated client credentials carry exactly the scopes its outbound edges need —
read from `[RequiresScope]`, `[Contract]`, `[Facility]`, and `[Api]` on the
operations it calls. Network policy and credentials then state the same graph
twice: a caller missing from the graph has neither a route nor a credential.

**End users.**

1. The user signs in through the auth vertical: login UI → auth API gateway →
   identity service → user store. That vertical alone holds the signing key.
2. The identity service issues a signed token carrying the user's claims and
   scopes.
3. The user calls a service vertical's Ingress gateway with the token. The
   gateway verifies the signature locally against the auth vertical's public key,
   which was provisioned into the gateway at deployment. It never calls the auth
   vertical, so no runtime request crosses sideways.
4. The gateway checks the token against the claims the endpoint declares
   (`[Api]` claims, `[RequiresScope]`, `[RequiresClaim]`) — the requirements are
   compile-time; only whether this user holds them is runtime — and passes a
   verified `AuthorizationContext` down the vertical. Services below trust the
   context because it arrives over an edge from an admitted service identity.

Key rotation is a deployment: new public keys ship with the next deployment, and
the identity service signs with overlapping keys across the switch. That keeps
it inside the "every new thing needs a deployment" rule. The alternative —
gateways introspecting tokens against the auth vertical at runtime — makes auth
the one vertical others may reach sideways.

## Open questions

1. **Shared lower nodes.** If two Internal services depend on the same
   data-access gateway, is that gateway in both verticals, or must sources be
   partitioned so each belongs to exactly one?
2. **Same-tier calls.** Does a service gateway calling its core service cross a
   tier, or is it a hop within one segment?
3. **Management traffic.** Monitoring and operations reach every tier. Is that a
   separate plane outside the tier model, or a tier of its own?
4. **Unmarked roles.** Front ends and third-party gateways have no marker yet.

## Gap from current code

As of this note:

- Zones are one axis: seven unordered markers with hand-written inbound and
  outbound lists in `[ZonePolicy]`. There is no tier order and no segment.
- Policies are rendered per zone, so every service in a zone reaches everything
  its zone may reach.
- The generated compose puts every container on the shared `atelier-network`
  in addition to any isolated or per-store network, which voids the isolation.
- Data-store networks come from smash infrastructure flags, not from the
  services that use a store, and only one Postgres and one Redis container are
  emitted per environment.
- The requisite manifest records products and assemblies, not dependency edges.
- `ATELIER0310` reads an outbound allow-list from `[NetworkZone]`, which has no
  such property, so it flags every cross-zone requisite.
