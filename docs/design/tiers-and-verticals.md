# Design: tiers × verticals

**Status:** proposed — not implemented. Nothing in this note describes current
behavior unless it says so.

## Principle: the topology is closed at compile time

Everything a deployment's network and service-auth posture depends on is known
when the code is built: which services exist, which scopes and claims each
operation requires, and which services each one depends on. Nothing new can
appear without a build, and nothing built can run without a deployment.

Atelier therefore treats the topology as a compile-time artifact. The build
affirms: *the access associated with this service is fixed, and will not change
until another build.* Network policy, service identity, and gateway routing are
generated from code, not configured beside it, and runtime authorization checks
a request against a decision the build already made rather than making one.

The closed world covers the *shape* — what each service may reach and what each
operation requires. Which end user holds which claim is still bound at runtime.

## The model

Two axes.

**Tiers** are horizontal and ordered, outermost first:

| Tier | Holds |
|---|---|
| Public | Public front ends; public user authentication. |
| Ingress (DMZ) | Public APIs for authentication and service access — the gateways. |
| Internal | Service gateways and core application services, segmented by service. |
| Data | Data-access gateways and third-party gateways, segmented by source. |

**Verticals** run down through the tiers. Segmentation starts at Ingress —
each gateway gets its own segment (subnet or equivalent) — and propagates
downward: the Internal services behind a gateway, and the data sources behind
those services, inherit that segmentation.

**The rule:** a request moves from tier *n* to tier *n+1*, and only along a
dependency the code declares. It falls straight through its vertical. A request
sideways into another vertical, across more than one tier, or back up, has no
route: it doesn't fail authorization, it reaches nothing.

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

   allowed:  (tier n, vertical v) ──► (tier n+1, vertical v), along a declared dependency
   no route: sideways, skipping a tier, upward
```

## Deriving the topology from code

No new hand-maintained topology declaration. The inputs already exist or sit
next to existing attributes:

- **Tier** — each service declares its tier with a marker type, as
  `[NetworkZone]` does today. Tiers are a closed, ordered set; the order is the
  only hand-written policy.
- **Edges** — the `[Requisite]` graph. A requisite is a declared dependency, so
  it is the complete list of who may call whom.
- **Verticals** — not declared. A vertical is what is reachable downward from an
  Ingress gateway along requisite edges. Data-tier segments are keyed by source.

The host generator already walks each host's services and products at compile
time. It would additionally emit the edge list — service, tier, and each
requisite target with its tier — and render everything below from that one
model.

## Compile-time enforcement

Violations are build errors, not runtime denials:

- A requisite whose target is not exactly one tier down is an error. Same-tier
  dependencies are allowed only within a service's own segment.
- A service without a tier is an error (today's `ATELIER0300` is a warning).
- An operation without its scope/claim requirement on a public-facing tier is an
  error.

These replace the zone-boundary check (`ATELIER0310`), which is defined in terms
of `[ZonePolicy]` rather than edges.

## Generated outputs

All derived from the edge model; none hand-edited.

**Kubernetes.** Every pod carries `io.atelier.tier` and `io.atelier.segment`
labels. Each namespace starts from default-deny. Each declared edge renders one
ingress rule on the target selecting exactly the source's tier and segment, and
the matching egress rule on the source. Policy count scales with edges, well
within CNI limits for any realistic deployment.

**Docker Compose.** No shared bridge. One network per edge boundary per segment;
each container joins the network above it and the networks below it, nothing
else. Network count is roughly segments × tier boundaries, so larger topologies
need the Docker daemon's `default-address-pools` widened beyond the default
(about 31 bridge networks).

**Service identity.** Per service: its audience, the scopes it may request, its
client registration, and its mTLS identity, all emitted from the same model, so
a credential cannot grant what the network forbids or vice versa.

## Authentication across verticals

Every vertical has to validate tokens, but auth is itself a vertical. Requests
must not cross sideways into it at runtime, so token validation runs inside each
vertical's gateway against signing keys provisioned at deployment — no runtime
call into the auth vertical.

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
- The requisite manifest records products and assemblies, not edges.
- `ATELIER0310` reads an outbound allow-list from `[NetworkZone]`, which has no
  such property, so it flags every cross-zone requisite.

## Open questions

1. **Shared lower nodes.** If two Internal services depend on the same
   data-access gateway, is that gateway in both verticals, or must sources be
   partitioned so each belongs to exactly one? The strict answer keeps verticals
   a forest; the permissive one makes them a DAG.
2. **Same-tier calls.** Does a service gateway calling its core service count as
   one tier or a within-segment hop? This note assumes the latter.
3. **Management traffic.** Monitoring and operations reach every tier. Is that a
   separate plane outside the tier model, or a tier of its own?
4. **Signing keys.** Key material is deployment-time, not compile-time. Its
   distribution path must not itself become a cross-vertical route.
