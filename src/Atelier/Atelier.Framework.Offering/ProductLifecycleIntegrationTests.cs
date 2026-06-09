using Atelier.Framework.Primitives;
using Atelier.Framework.Context;
using Atelier.Framework.Infrastructure;
using Atelier.Framework.Attributes;
using Atelier.Framework.Observability;
using Atelier.Framework.Observability.Strategy;
using Atelier.Framework.Offering.Product;
using Atelier.Framework.Offering.Product.Configuration;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;
using Atelier.Framework.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Atelier.Framework.Offering;

public sealed class LifecycleProbeContextAccessor : IContextAccessor
{
    private IContext _current = global::Atelier.Framework.Context.Context.Empty;

    public IContext Current => _current;

    public void SetCurrent(IContext context)
    {
        _current = context;
    }
}

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public sealed class LifecycleProbeService
{
    public string Echo(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return $"echo:{value}";
    }
}

public sealed class LifecycleProbeCounters
{
    private int _startCount;
    private int _stopCount;

    public int StartCount => Volatile.Read(ref _startCount);
    public int StopCount => Volatile.Read(ref _stopCount);

    public void RecordStart()
    {
        Interlocked.Increment(ref _startCount);
    }

    public void RecordStop()
    {
        Interlocked.Increment(ref _stopCount);
    }
}

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class LifecycleProbeOffering : OfferingBase
{
    [Requisite] private readonly LifecycleProbeService _service = null!;

    private readonly LifecycleProbeCounters _counters = new();

    public int StartCount => _counters.StartCount;
    public int StopCount => _counters.StopCount;

    protected override void OnStart()
    {
        _counters.RecordStart();
    }

    protected override void OnStop()
    {
        _counters.RecordStop();
    }

    internal Task<Outcome<string>> InvokeAsync(string value)
    {
        if (value is null)
        {
            return Task.FromResult(Outcome<string>.Failure());
        }
        return Task.FromResult(Outcome<string>.Success(_service.Echo(value)));
    }
}

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class LifecycleProbeProduct : ProductBase
{
    protected override void ConfigureOfferings(IOfferingConfiguration offerings)
    {
        offerings.AddOffering<LifecycleProbeOffering>();
    }

    protected override Task<Outcome> OnStartAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Outcome.Success());
    }

    protected override Task<Outcome> OnStopAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Outcome.Success());
    }
}

public static class ProductLifecycleIntegrationTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggingStrategy, ConsoleLoggingStrategy>();
        services.AddSingleton<IContextAccessor, LifecycleProbeContextAccessor>();
        services.AddSingleton<ILogger>(sp => new Logger(sp.GetRequiredService<IContextAccessor>(),
                                                        sp.GetRequiredService<ILoggingStrategy>()));
        services.AddSingleton<IOfferingProvider, ServiceProviderOfferingProvider>();
        services.AddSingleton<LifecycleProbeService>();
        services.AddSingleton<LifecycleProbeOffering>();
        services.AddSingleton<LifecycleProbeProduct>();
        return services.BuildServiceProvider();
    }

    [GeneratedTest("Lifecycle/Product-Start-Invoke-Stop-Succeeds", "global::Atelier.Framework.Offering.LifecycleProbeProduct")]
    public static async Task ProductStartInvokeStopDrivesOfferingLifecycle()
    {
        using var provider = BuildProvider();
        var product = provider.GetRequiredService<LifecycleProbeProduct>();
        var offering = provider.GetRequiredService<LifecycleProbeOffering>();

        if (product.IsRunning)
        {
            throw new InvalidOperationException("product reported running before StartAsync");
        }

        var started = await product.StartAsync().ConfigureAwait(false);
        if (!started.IsSuccess)
        {
            throw new InvalidOperationException("StartAsync failed");
        }
        if (!product.IsRunning)
        {
            throw new InvalidOperationException("product did not report running after StartAsync");
        }
        if (offering.StartCount != 1
            || !offering.IsRunning)
        {
            throw new InvalidOperationException($"offering was not started by the product (startCount={offering.StartCount}, running={offering.IsRunning})");
        }

        var invoked = await offering.InvokeAsync("payload").ConfigureAwait(false);
        if (!invoked.IsSuccess)
        {
            throw new InvalidOperationException("offering operation failed");
        }
        if (invoked.Data != "echo:payload")
        {
            throw new InvalidOperationException($"operation returned '{invoked.Data}', expected 'echo:payload'");
        }

        var stopped = await product.StopAsync().ConfigureAwait(false);
        if (!stopped.IsSuccess)
        {
            throw new InvalidOperationException("StopAsync failed");
        }
        if (product.IsRunning)
        {
            throw new InvalidOperationException("product still reported running after StopAsync");
        }
        if (offering.StopCount != 1
            || offering.IsRunning)
        {
            throw new InvalidOperationException($"offering was not stopped by the product (stopCount={offering.StopCount}, running={offering.IsRunning})");
        }
    }

    [GeneratedTest("Lifecycle/Double-Start-Is-Rejected", "global::Atelier.Framework.Offering.LifecycleProbeProduct")]
    public static async Task SecondStartReportsAlreadyRunning()
    {
        using var provider = BuildProvider();
        var product = provider.GetRequiredService<LifecycleProbeProduct>();

        var first = await product.StartAsync().ConfigureAwait(false);
        if (!first.IsSuccess)
        {
            throw new InvalidOperationException("first StartAsync failed");
        }

        var second = await product.StartAsync().ConfigureAwait(false);
        if (second.IsSuccess)
        {
            throw new InvalidOperationException("second StartAsync unexpectedly succeeded");
        }
        if (!product.IsRunning)
        {
            throw new InvalidOperationException("product stopped reporting running after a rejected second StartAsync");
        }

        await product.StopAsync().ConfigureAwait(false);
    }
}
