# Products

A **Product** composes Offerings and owns their lifecycle. It derives from `ProductBase`, lists its offerings in `ConfigureOfferings`, and implements the `OnStartAsync` / `OnStopAsync` hooks.

## Defining a Product

```csharp
[Infrastructure(InfrastructureLifetime.Singleton)]
public partial class GreetingProduct : ProductBase
{
    [Requisite] private readonly GreetingOffering _greeting = null!;

    protected override void ConfigureOfferings(IOfferingConfiguration offerings)
    {
        offerings.AddOffering(_greeting);
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

- **`StartAsync(CancellationToken)` / `StopAsync(CancellationToken)`** — return `Task<Outcome>`; they run `ConfigureOfferings` and invoke `OnStartAsync` / `OnStopAsync`.
- **`ConfigureOfferings(IOfferingConfiguration)`** — register the offerings the product owns with `AddOffering(offering)`. A product receives its offerings as `[Requisite]` members, so each one is a declared edge.
- **`ConfigureServices` / `ConfigureEndpoints` / `ConfigureFacilities`** — optional overrides for extra DI registrations, endpoint mappings, and facilities.

## Running a Product

The host wires the core services (`IContextAccessor`, `ILogger`, and the offerings) and drives the product lifecycle. The complete, runnable bootstrap is in [`src/Atelier/examples/example-showcase`](../../src/Atelier/examples/example-showcase):

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

In a deployed application the host is a checked-in project with no `Program.cs` that sets `<AtelierBoutiqueHost>true</AtelierBoutiqueHost>`. It declares the boutique and its products as assembly attributes, and `BoutiqueHostSourceGenerator` emits the entry point at compile time — DI registrations for `[Infrastructure]` types, `[Api]` endpoint maps, and the product manifest — which `BoutiqueHost.RunAsync` runs:

```csharp
[assembly: Boutique("orders", Name = "Orders")]
[assembly: BoutiqueProduct(typeof(OrdersProduct))]
```

See `examples/example-host` for a complete host.

## See also

- [Offerings](offerings.md) — the units a product composes.
- [Requisites](requisites.md) — how the generated constructors fill dependencies.
