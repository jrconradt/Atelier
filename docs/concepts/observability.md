# Observability and IAtelier

Components observe themselves through `IAtelier`, not through a logger they hold. A type declares `: IAtelier`, and the requisites generator emits the `Logger` field plus the `Observe(...)` plumbing into a generated partial. Components do not hold a logger or call `Console.WriteLine`; they observe through `Observe(...)` rather than driving a fluent `Logger.X().Y()` chain.

## The contract

`IAtelier` is a single method:

```csharp
public interface IAtelier
{
    public void Observe(
        LogLevel level = LogLevel.Information,
        Exception? exception = null,
        string? message = null,
        params ReadOnlySpan<(string Key, object Value)> values);
}
```

## Observing

A partial class declaring `: IAtelier` calls `Observe(...)` directly — the generated partial supplies the implementation:

```csharp
public partial class GreetingOffering : OfferingBase
{
    protected override void OnStart()
    {
        Observe(LogLevel.Information);
    }

    private void OnReject(string reason)
    {
        Observe(LogLevel.Warning, values: [("Reason", reason)]);
    }
}
```

`Observe` takes a `LogLevel`, an optional `Exception`, an optional `message`, and a `params` span of `(string Key, object Value)` structured-value tuples. The call site names what happened through the level and the values; the generator wires the rest.

## What sits behind Observe

`Atelier.Framework.Observability` carries the strategies and formatters the generated plumbing dispatches to — console, structured, file, Elasticsearch, and composite logging strategies; JSON, compact, and plain-text formatters; sensitive-value redaction; and `LoggingContext` for ambient correlation. Components do not pick these directly; they `Observe`, and the configured strategy decides the sink.

## See also

- [Offerings](offerings.md) — offerings observe their lifecycle.
- [Requisites](requisites.md) — the generator that emits `Observe`.
