using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Atelier.Framework.Facility.Configuration;

public static class FacilityConfigurationExtensions
{
    public static IServiceCollection AddFacilityConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<FacilityConfiguration>(
            configuration.GetSection(FacilityConfiguration.SECTION_NAME));

        services.AddSingleton(sp =>
        {
            var config = new FacilityConfiguration();
            configuration.GetSection(FacilityConfiguration.SECTION_NAME).Bind(config);
            return config;
        });

        services.AddSingleton(sp =>
            sp.GetRequiredService<FacilityConfiguration>().Remote);

        return services;
    }
}
