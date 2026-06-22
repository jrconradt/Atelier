# Atelier

[![CI](https://github.com/jrconradt/Atelier/actions/workflows/ci.yml/badge.svg)](https://github.com/jrconradt/Atelier/actions/workflows/ci.yml) ![.NET](https://img.shields.io/badge/.NET-10-512BD4) [![License](https://img.shields.io/badge/License-Apache_2.0-blue)](LICENSE)

Infrastructure host and orchestration framework for .NET 10.

Hosts run Offerings (units of service). Offerings compose into Products. Products wire together via Requisites — dependency injection driven by Roslyn source generators rather than runtime container plumbing. Result-typed contracts (`Outcome` / `Outcome<T>`) flow through messaging, an event stream, and state machines, with cross-cutting concerns for identity, network, queueing, observability, performance, and resilience.

## Hello, Offering

An Offering is a `partial` class deriving from `OfferingBase`. Dependencies are declared as `[Requisite]` fields; the generators emit the constructor that fills them. Operations return an `Outcome<T>`.

```csharp
[NetworkZone(typeof(Application))]
[Infrastructure(InfrastructureLifetime.Singleton)]
public partial class GreetingOffering : OfferingBase
{
    [Requisite] private readonly GreetingService _greetings = null!;

    protected override void OnStart()
    {
        Observe(LogLevel.Information);
    }

    protected override void OnStop()
    {
        Observe(LogLevel.Information);
    }

    [Operation("Greet")]
    public Task<Outcome<string>> GreetAsync(string name,
                                            CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Outcome<string>.Failure());
        }

        if (name is null)
        {
            return Task.FromResult(Outcome<string>.Failure());
        }

        return Task.FromResult(Outcome<string>.Success(_greetings.Compose(name)));
    }
}
```

A Product composes offerings and owns their lifecycle:

```csharp
[Infrastructure(InfrastructureLifetime.Singleton)]
public partial class GreetingProduct : ProductBase
{
    protected override void ConfigureOfferings(IOfferingConfiguration offerings)
    {
        offerings.AddOffering<GreetingOffering>();
    }

    protected override Task<Outcome> OnStartAsync(CancellationToken cancellationToken)
    {
        Observe(LogLevel.Information);
        return Task.FromResult(Outcome.Success());
    }

    protected override Task<Outcome> OnStopAsync(CancellationToken cancellationToken)
    {
        Observe(LogLevel.Information);
        return Task.FromResult(Outcome.Success());
    }
}
```

The complete, runnable version — including the host bootstrap — is in [`src/Atelier/examples/example-showcase`](src/Atelier/examples/example-showcase).

## Layout

Projects under `src/Atelier/` are grouped by concern:

| Concern | Projects |
|---|---|
| Programming model | `Atelier.Framework.Contract`, `Atelier.Framework.Context`, `Atelier.Framework.Properties`, `Atelier.Framework.Outcomes`, `Atelier.Framework.Strategy`, `Atelier.Framework.Infrastructure`, `Atelier.Framework.Attributes`, `Atelier.Framework.Primitives` |
| Execution and hosting | `Atelier.Framework.Host.Execution`, `Atelier.Framework.Offering`, `Atelier.Framework.Requisitions`, `Atelier.Framework.Api` |
| Events and state | `Atelier.Framework.EventStream`, `Atelier.Framework.StateMachine` |
| Communication | `Atelier.Framework.Messaging`, `Atelier.Framework.Network`, `Atelier.Framework.Queueing` |
| Cross-cutting | `Atelier.Framework.Identity`, `Atelier.Framework.Identity.Authorization`, `Atelier.Framework.Facility`, `Atelier.Framework.Observability`, `Atelier.Framework.Performance`, `Atelier.Framework.Resilience`, `Atelier.Framework.Attache` |
| Facilities | `Atelier.Facilities.Cache`, `Atelier.Facilities.Cache.InMemory`, `Atelier.Facilities.Cache.Redis` |
| Build, codegen, analysis | `Atelier.Build`, `Atelier.Framework.Analyzers`, `Atelier.Framework.Analyzers.CodeFixes`, and `Atelier.Framework.*.Generators` |
| Testing | `Atelier.Framework.Testing`, `Atelier.Framework.Testing.Contract`, `Atelier.Framework.Test.Generators` |
| Examples | `examples/example-bench`, `examples/example-showcase` |
| Benchmarks | `benchmarks/cache-bench`, `benchmarks/context-bench`, `benchmarks/eventstream-bench`, `benchmarks/messaging-bench`, `benchmarks/network-bench`, `benchmarks/outcomes-bench`, `benchmarks/performance-bench`, `benchmarks/queueing-bench`, `benchmarks/requisitions-bench`, `benchmarks/statemachine-bench` |

## Build

```bash
dotnet build src/Atelier/Atelier.slnx
```

## Test

Atelier ships a custom test harness rather than `dotnet test`, run through the `smash` build tool:

```bash
dotnet run --project src/Atelier/Atelier.Build -- test
```

It discovers `[GeneratedTest]` fixtures from the compiled assemblies and reports a `Total / Pass / Fail / NeedsFixture` summary.

## Source generators

Atelier leans on Roslyn source generators over hand-wired DI. `Atelier.Framework.*.Generators` emit constructor injection for `[Requisite]` fields, `Logger` + `Observe(...)` plumbing for `IAtelier` partial classes, product/offering wiring, contract glue, transport code, and generated tests. `Atelier.Framework.Analyzers` enforces the corresponding patterns at compile time (`ATELIER*` diagnostics).

`Atelier.Build` ships non-Roslyn generators under `Generation/` that emit Dockerfiles, `docker-compose.yml`, `Program.cs` scaffolding, and mermaid diagrams. `smash` is the boutique build tool, packaged from `Atelier.Build` (`PackAsTool`, command `smash`):

```bash
dotnet run --project src/Atelier/Atelier.Build -- --help
```

## Documentation

Concept guides and the diagnostics reference live in [`docs/`](docs/README.md).

## Contributing

See [`docs/reference/smash.md`](docs/reference/smash.md) for the build and test workflow.

## Security

To report a vulnerability, see [SECURITY.md](SECURITY.md).

## Status

Active development; APIs and `ATELIER*` diagnostics are still evolving.

## License

Apache-2.0. Copyright 2026 Infalligence Labs LLC — see [LICENSE](LICENSE).
