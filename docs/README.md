# Atelier Documentation

Atelier is an infrastructure host and orchestration framework for .NET 10. Hosts run **Offerings**, Offerings compose into **Products**, dependencies are wired by Roslyn source generators reading **`[Requisite]`** fields, and **`Outcome` / `Outcome<T>`** is the result contract that flows through the system.

New here? Read [`src/Atelier/examples/example-showcase`](../src/Atelier/examples/example-showcase) — a complete, runnable Offering → Product → `Outcome` walkthrough — alongside these pages.

## Concepts

| Page | Topic |
|---|---|
| [Offerings](concepts/offerings.md) | Units of service: `OfferingBase`, `[Operation]`, lifecycle |
| [Products](concepts/products.md) | Composing offerings via `ProductBase` and `ConfigureOfferings` |
| [Requisites](concepts/requisites.md) | Source-generated dependency injection with `[Requisite]` |
| [Outcomes](concepts/outcomes.md) | The `Outcome` / `Outcome<T>` result contract |
| [Messaging](concepts/messaging.md) | Request/response dispatch via `IRequestHandler` and `IHandlerRegistry` |
| [Event stream](concepts/eventstream.md) | Topic-based consumers, offset tracking, and at-least-once delivery |
| [Network zones](concepts/network.md) | Zero-trust zones, `[NetworkZone]`, and connection policy |
| [Facilities](concepts/facilities.md) | The Facility / Attache / Gateway model for infrastructure capabilities |
| [Observability](concepts/observability.md) | `IAtelier` and the `Observe(...)` contract |

## Reference

| Page | Topic |
|---|---|
| [Diagnostics](reference/diagnostics.md) | The `ATELIER*` analyzer diagnostics |
| [`smash` build tool](reference/smash.md) | Building with smash: verbs, boutiques, `smash.yml`, and the command reference |

## Not yet documented

These subsystems ship in the framework but do not yet have concept pages here. Until they do, the source and its tests under `src/Atelier/` are the reference for their contracts.

| Subsystem | Project |
|---|---|
| Resilience (retry, circuit-breaker, timeout) | `Atelier.Framework.Resilience` |
| Performance (budgets, instrumentation, Prometheus export) | `Atelier.Framework.Performance` |
| Queueing | `Atelier.Framework.Queueing` |
| State machine (snapshot, migrator, coordinator) | `Atelier.Framework.StateMachine` |
| Identity (principal, claims, JWT/OIDC) | `Atelier.Framework.Identity` |
| Contract versioning and migration | `Atelier.Framework.Contract` |
| Strategy primitives | `Atelier.Framework.Strategy` |

## Build and test

```bash
dotnet build src/Atelier/Atelier.slnx
dotnet run --project src/Atelier/Atelier.Build -- test
```

The `smash` tool drives boutique builds, the test harness, benchmarks, and the
Docker pipeline — see [`reference/smash.md`](reference/smash.md) for the full
build workflow.
