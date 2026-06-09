# Outcomes

Atelier uses explicit result types instead of exceptions for operation results. `Outcome` represents a fallible action; `Outcome<T>` carries data on success. Both are value types that model success or failure as a flag — they carry no error message or error code.

## Outcome

```csharp
var ok = Outcome.Success();
var bad = Outcome.Failure();

if (ok.IsSuccess)
{
    Proceed();
}
```

Surface: `IsSuccess`, plus value equality (`==`, `!=`, `Equals`). `Outcome` is a `struct` and carries no payload; failure is a bare flag.

## Outcome&lt;T&gt;

```csharp
var ok = Outcome<int>.Success(42);
var bad = Outcome<int>.Failure();

if (ok.IsSuccess)
{
    var value = ok.Data;
}
```

Surface: `Data`, `IsSuccess`, `IsDefault`, and `Deconstruct(out T? data, out bool isSuccess)`.

## Deconstruction and implicit conversions

Deconstruct an `Outcome<T>` into its data and success flag:

```csharp
var (data, isSuccess) = outcome;
```

A `bool` lifts to an `Outcome`, and a value of `T` lifts to a successful `Outcome<T>`:

```csharp
Outcome success = true;
Outcome<string> value = "hello";
```

## Composition

`OutcomeExtensions` exposes railway-oriented composition over `Outcome` and `Outcome<T>`:

```csharp
Outcome<int> parsed = Outcome<string>.Success("42")
    .Bind(s => int.TryParse(s, out var n)
        ? Outcome<int>.Success(n)
        : Outcome<int>.Failure());

Outcome<int> doubled = parsed.Map(n => n * 2);

string rendered = doubled.Match(
    onSuccess: n => $"value {n}",
    onFailure: () => "failed");
```

- `Bind<T, U>(Func<T, Outcome<U>>)` — chains a fallible step; short-circuits to `Outcome<U>.Failure()` on failure.
- `BindAsync` — three overloads: `Outcome<T>` + `Func<T, Task<Outcome<U>>>`, `Task<Outcome<T>>` + `Func<T, Outcome<U>>`, and `Task<Outcome<T>>` + `Func<T, Task<Outcome<U>>>`.
- `Map<T, U>(Func<T, U>)` and `MapAsync(Task<Outcome<T>>, Func<T, U>)` — transform the success value; failures pass through unchanged.
- `Match` — collapses to a single value; one overload for `Outcome<T>` (`Func<T, R>` success, `Func<R>` failure) and one for `Outcome` (`Func<R>` success, `Func<R>` failure).
- `Tap<T>(Action<T>)` — runs a side effect on success and returns the outcome unchanged.
- `OnFailure(Action)` — runs a side effect on failure and returns the outcome unchanged; overloads for both `Outcome` and `Outcome<T>`.
- `TunnelFailure` — reprojects a failure to a different result type; one overload from `Outcome<T>` and one from `Outcome`.

`Value<T>()`, `IsFailure()`, and the `ToOutcome()` / `ToOutcomeTask()` lift helpers round out the surface.

## Why not exceptions?

- **Explicit** — the signature shows failure is possible; no surprise throws.
- **Cheap** — `Outcome` is a struct; the happy path does not allocate.
- **Testable** — assert success or failure without try/catch.

The analyzers enforce the pattern: `OutcomePatternEnforcementAnalyzer` (`ATELIER1000`–`ATELIER1002`) requires returning an `Outcome` rather than throwing in operation code. Exceptions remain for genuinely exceptional faults.

## See also

- [Offerings](offerings.md) — operations return outcomes.
- [Diagnostics](../reference/diagnostics.md) — `ATELIER1000`–`ATELIER1002`.
