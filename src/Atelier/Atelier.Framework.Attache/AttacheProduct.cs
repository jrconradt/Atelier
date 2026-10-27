using Atelier.Framework.Primitives;
using Atelier.Framework.Infrastructure;
using Atelier.Framework.Attributes;
using Atelier.Framework.Observability;
using Atelier.Framework.Offering;
using Atelier.Framework.Offering.Product;
using Atelier.Framework.Offering.Product.Configuration;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;
using Microsoft.Extensions.DependencyInjection;

namespace Atelier.Framework.Attache;

[Infrastructure(InfrastructureLifetime.Singleton)]
public partial class AttacheProduct : ProductBase
{
    [Requisite] private readonly AttacheRuntimeOffering _runtime = null!;

    protected override void ConfigureOfferings(IOfferingConfiguration offerings)
    {
        offerings.AddOffering(_runtime);
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<AttacheHost>();
        services.AddHostedService<AttacheHost>();
    }

    protected override void ConfigureEndpoints(IEndpointConfiguration endpoints)
    {
        endpoints.MapOperations<IAttacheRuntimeOffering>("/api/attache");
    }

    protected override async Task<Outcome> OnStartAsync(CancellationToken cancellationToken)
    {
        Observe(LogLevel.Information);

        return Outcome.Success();
    }

    protected override async Task<Outcome> OnStopAsync(CancellationToken cancellationToken)
    {
        Observe(LogLevel.Information);

        return Outcome.Success();
    }
}
