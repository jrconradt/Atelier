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

## Direct Topology Wiring

Rather than routing dependencies through intermediate generic wrappers or redirection layers, the compile-time topology engine resolves interface-based `[Requisite]` fields directly:

- **No generic facility wrappers**: Offerings declare dependencies directly on the interfaces they require (e.g. `IRedisConnectionProvider`, `IDbConnection`).
- **Direct Resolution**: The generated DI container maps these interfaces directly to their compiled, concrete implementation classes.
- **Zero Overhead**: This ensures there is no runtime redirect overhead, and concrete generated classes are wired directly into the host execution context.

## Validation

`RequisiteDependencyAnalyzer` (`ATELIER0600`) flags a `[Requisite]` whose dependency has no discoverable registration, turning a runtime resolution failure into a compile-time diagnostic.

## See also

- [Offerings](offerings.md) and [Products](products.md) — the types that carry requisites.
- [Diagnostics](../reference/diagnostics.md) — the analyzer rules that guard the pattern.
