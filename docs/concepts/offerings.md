# Offerings

An **Offering** is a unit of service. It derives from `OfferingBase`, declares its dependencies as `[Requisite]` fields, and exposes operations that return an `Outcome` / `Outcome<T>`.

## Defining an Offering

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

The pieces:

- **`partial`** — required. The source generators add the constructor (filling `[Requisite]` fields) and the `Logger` / `Observe(...)` plumbing in a second partial.
- **`[Infrastructure(InfrastructureLifetime.…)]`** — registers the offering and declares its DI lifetime (`Singleton`, `Scoped`, `Transient`).
- **`[NetworkZone(typeof(…))]`** — every offering declares the network zone it runs in; the zone is a marker type from `Atelier.Framework.Primitives` (`Application`, `Internal`, `External`, `Web`, `Security`, `Data`, `Management`), not an enum value. A missing zone is flagged by `ATELIER0300`. See [Network zones](network.md).
- **`[Requisite]`** — a dependency the generators inject. See [Requisites](requisites.md).
- **`OnStart` / `OnStop`** — lifecycle hooks invoked when the owning Product starts and stops.
- **`[Operation("…")]`** — marks an operation. The generators emit traced/validated wrappers; operations must guard their `CancellationToken` and reference parameters at entry (enforced by `ATELIER1310` / `ATELIER003`).

## Observability

`Observe(...)` is generated onto every `IAtelier` partial. Call it with a `LogLevel` and optional structured values:

```csharp
Observe(LogLevel.Information);
Observe(LogLevel.Warning, values: [("Reason", reason)]);
```

## See also

- [Products](products.md) — composing offerings.
- [Requisites](requisites.md) — how dependencies are wired.
- [Outcomes](outcomes.md) — the operation result contract.
