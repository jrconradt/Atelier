using Atelier.Framework.Primitives;
using Atelier.Framework.Context;
using Atelier.Framework.Attributes;
using Atelier.Framework.Infrastructure.Operation;
using Atelier.Framework.Network;
using Atelier.Framework.Observability;
using Atelier.Framework.Observability.Strategy;
using Atelier.Framework.Offering;
using Atelier.Framework.Offering.Product;
using Atelier.Framework.Offering.Product.Configuration;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;
using Microsoft.Extensions.DependencyInjection;

namespace Atelier.Example.Showcase;

internal static class Program
{
    private static async Task<int> Main()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggingStrategy, ConsoleLoggingStrategy>();
        services.AddSingleton<IContextAccessor, ShowcaseContextAccessor>();
        services.AddSingleton<ILogger>(sp => new Logger(sp.GetRequiredService<IContextAccessor>(),
                                                        sp.GetRequiredService<ILoggingStrategy>()));
        services.AddSingleton<IOfferingProvider, ServiceProviderOfferingProvider>();
        services.AddSingleton<GreetingService>();
        services.AddSingleton<GreetingOffering>();
        services.AddSingleton<GreetingProduct>();

        var provider = services.BuildServiceProvider();
        var product = provider.GetRequiredService<GreetingProduct>();

        var started = await product.StartAsync().ConfigureAwait(false);
        if (!started.IsSuccess)
        {
            Console.WriteLine("product failed to start");
            return 1;
        }

        var greeter = provider.GetRequiredService<GreetingOffering>();
        var greeting = await greeter.GreetAsync("world", CancellationToken.None).ConfigureAwait(false);
        if (greeting.IsSuccess)
        {
            Console.WriteLine(greeting.Data);
        }
        else
        {
            Console.WriteLine("greet failed");
        }

        await product.StopAsync().ConfigureAwait(false);
        return greeting.IsSuccess ? 0 : 1;
    }
}

public sealed class ShowcaseContextAccessor : IContextAccessor
{
    private IContext _current = Context.Empty;

    public IContext Current => _current;

    public void SetCurrent(IContext context)
    {
        _current = context;
    }
}

[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
[Infrastructure(InfrastructureLifetime.Singleton)]
public sealed class GreetingService
{
    public string Compose(string name)
    {
        return $"Hello, {name}!";
    }
}

[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
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
            Observe(LogLevel.Warning);
            return Task.FromResult(Outcome<string>.Failure());
        }

        return Task.FromResult(Outcome<string>.Success(_greetings.Compose(name)));
    }
}

[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
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
