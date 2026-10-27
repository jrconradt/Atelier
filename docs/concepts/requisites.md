# Requisites

A **Requisite** is a dependency that Atelier injects at compile time. Mark a field with `[Requisite]`; the `RequisiteInjectionSourceGenerator` emits a constructor — in a generated partial — that fills it. There is no runtime container reflection over your type.

## Declaring a dependency

```csharp
[Infrastructure(InfrastructureLifetime.Singleton)]
public partial class GreetingOffering : OfferingBase
{
    [Requisite] private readonly GreetingService _greetings = null!;
}
```

What happens:

- The class is **`partial`** so the generator can add a second part.
- The generator emits a constructor taking `GreetingService` and assigning `_greetings`.
- The `= null!` initializer satisfies nullable analysis for the field the generator fills.

The dependency itself is an ordinary `[Infrastructure]`-registered service or another offering:

```csharp
[Infrastructure(InfrastructureLifetime.Singleton)]
public sealed class GreetingService
{
    public string Compose(string name)
    {
        return $"Hello, {name}!";
    }
}
```

## Lifetimes

`[Infrastructure(InfrastructureLifetime.…)]` declares how the service is registered:

| Lifetime | Meaning |
|---|---|
| `Singleton` | One instance for the process. |
| `Scoped` | One instance per scope (e.g. per request). |
| `Transient` | A new instance per resolution. |

`Atelier.Framework.Requisitions.Generators` also emits factories for these lifetimes (`FactorySourceGenerator`) and registration wiring, so the host can construct types with their requisites resolved.

## Dependency edges

Every `[Requisite]` is a declared edge from a service to the type it depends on.
The requisites generator records each one as
`[assembly: RequisiteEdge(typeof(Service), typeof(Requisite))]` in the service's
own assembly, so the edge survives into metadata even though the member is
private. A boutique host's generator reads those declarations (and its own
source directly) and writes every service's identity — its `Namespace.ServiceName`
— into `requisite-manifest.json` with the identities of the host services it
requires. Each requisite resolves to the service that provides it; the manifest
names services, never interfaces, and a requisite no host service provides is
not an edge.

## Validation

`RequisiteDependencyAnalyzer` (`ATELIER0600`) flags a `[Requisite]` whose dependency has no discoverable registration, turning a runtime resolution failure into a compile-time diagnostic.

## See also

- [Offerings](offerings.md) and [Products](products.md) — the types that carry requisites.
- [Diagnostics](../reference/diagnostics.md) — the analyzer rules that guard the pattern.
