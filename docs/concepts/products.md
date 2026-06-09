# Products

A **Product** composes Offerings and owns their lifecycle. It derives from `ProductBase`, lists its offerings in `ConfigureOfferings`, and implements the `OnStartAsync` / `OnStopAsync` hooks.

## Defining a Product

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

`ProductBase` provides the public lifecycle surface:

- **`StartAsync(CancellationToken)` / `StopAsync(CancellationToken)`** — return `Task<Outcome>`; they run `ConfigureOfferings`, resolve each offering through the `OfferingProvider`, and invoke `OnStartAsync` / `OnStopAsync`.
- **`ConfigureOfferings(IOfferingConfiguration)`** — register the offerings the product owns with `AddOffering<TOffering>()`.
- **`ConfigureServices` / `ConfigureEndpoints` / `ConfigureFacilities`** — optional overrides for extra DI registrations, endpoint mappings, and facilities.

## Running a Product

The host wires the core services (`IContextAccessor`, `ILogger`, `IOfferingProvider`, and the offerings) and drives the product lifecycle. The complete, runnable bootstrap is in [`src/Atelier/examples/example-showcase`](../../src/Atelier/examples/example-showcase):

```csharp
var product = provider.GetRequiredService<GreetingProduct>();

var started = await product.StartAsync().ConfigureAwait(false);
if (!started.IsSuccess)
{
    return 1;
}

var greeter = provider.GetRequiredService<GreetingOffering>();
var greeting = await greeter.GreetAsync("world", CancellationToken.None).ConfigureAwait(false);

await product.StopAsync().ConfigureAwait(false);
```

In a deployed application, `smash` (the `Atelier.Build` tool) generates this `Program.cs` host from the discovered Product.

## See also

- [Offerings](offerings.md) — the units a product composes.
- [Requisites](requisites.md) — how the generated constructors fill dependencies.
