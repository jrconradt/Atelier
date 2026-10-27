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

### Verticals

The segmentation that divides the Ingress gateways propagates downward. Each
segmentation layer creates a clearly delineated vertical: a request for a
resource falls straight through its vertical, and a request into another layer's
segment gets nothing.

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

## Proposed, not decided

Each item below is a candidate way to realize the decided design.

- **Where verticals come from.** Derive them from the `[Requisite]` graph — a
  vertical is what is reachable downward from an Ingress gateway along declared
  dependencies — rather than declaring them. The host generator already walks
  each host's services at compile time and would emit the edge list.
- **Where tiers come from.** A tier marker on each service, as `[NetworkZone]`
  does today, with the tier order as the only hand-written policy.
- **Compile-time enforcement.** Build errors rather than runtime denials: a
  dependency that is not exactly one tier down; a service with no tier (today's
  `ATELIER0300` is a warning); an operation on a public-facing tier without its
  scope/claim requirement. These would replace `ATELIER0310`.
- **Kubernetes.** `io.atelier.tier` and `io.atelier.segment` labels on every pod,
  default-deny per namespace, and one ingress/egress rule pair per declared edge.
- **Docker Compose.** No shared bridge; one network per tier boundary per
  segment, each container joining only the networks above and below it. Larger
  topologies would need the Docker daemon's `default-address-pools` widened
  beyond the default of about 31 bridge networks.
- **Service identity.** Audience, requestable scopes, client registration, and
  mTLS identity emitted per service from the same model as the network policy.
- **Authentication across verticals.** Token validation inside each vertical's
  gateway against signing keys provisioned at deployment, so no request crosses
  sideways into the auth vertical. The alternative is to make auth the one
  vertical others may reach sideways.

## Open questions

1. **Shared lower nodes.** If two Internal services depend on the same
   data-access gateway, is that gateway in both verticals, or must sources be
   partitioned so each belongs to exactly one?
2. **Same-tier calls.** Does a service gateway calling its core service cross a
   tier, or is it a hop within one segment?
3. **Management traffic.** Monitoring and operations reach every tier. Is that a
   separate plane outside the tier model, or a tier of its own?
4. **Signing keys.** Key material is deployment-time, not compile-time. How is it
   distributed without itself becoming a cross-vertical route?

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
