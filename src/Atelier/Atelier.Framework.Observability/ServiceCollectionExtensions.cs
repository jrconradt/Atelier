using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Atelier.Framework.Observability;

public static class ObservabilityServiceCollectionExtensions
{
    public static IServiceCollection AddAtelierObservability(
        this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddAtelierHealthChecks();

        return services;
    }

    public static IHealthChecksBuilder AddAtelierHealthChecks(
        this IHealthChecksBuilder builder)
    {
        builder.AddCheck(
            "self",
            () => HealthCheckResult.Healthy("Process is live."),
            tags: new[] { "liveness" });

        return builder;
    }
}
