using Atelier.Framework.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Atelier.Framework.Attache.Lifecycle;

public static class BoutiqueStartupServiceCollectionExtensions
{
    private const int SHUTDOWN_GRACE_BUFFER_SECONDS = 10;

    public static IServiceCollection AddAtelierBoutiqueStartup(
        this IServiceCollection services,
        BoutiqueManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        services.TryAddSingleton<BoutiqueStartupState>();

        services.Configure<HostOptions>(options =>
        {
            options.ShutdownTimeout = BoutiqueDrain.ResolveDrainWindow() + TimeSpan.FromSeconds(SHUTDOWN_GRACE_BUFFER_SECONDS);
        });

        services.AddSingleton<IHostedService>(provider =>
            new BoutiqueStartupHostedService(
                provider,
                provider.GetRequiredService<IHostApplicationLifetime>(),
                provider.GetService<ILogger>()).Configure(
                manifest,
                provider.GetRequiredService<BoutiqueStartupState>()));

        services.AddHealthChecks()
            .AddAtelierHealthChecks()
            .AddCheck<BoutiqueReadinessHealthCheck>(
                "boutique-readiness",
                failureStatus: HealthStatus.Unhealthy,
                tags: new[] { "readiness" });

        return services;
    }
}
