using Atelier.Framework.Attributes;
using Atelier.Framework.Identity.Configuration;
using Atelier.Framework.Identity.Interfaces;
using Atelier.Framework.Identity.Middleware;
using Atelier.Framework.Identity.Services;
using Atelier.Framework.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Atelier.Framework.Infrastructure;
using Microsoft.Extensions.Options;

namespace Atelier.Framework.Identity.Extensions;

[ContractAttribute("OidcServiceCollectionExtensions", Version = "1.0")]
public static class OidcServiceCollectionExtensions
{
    public static IServiceCollection AddOidcIdentity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<OidcConfiguration>, OidcConfigurationValidator>();
        services.AddOptions<OidcConfiguration>()
            .Bind(configuration.GetSection("Oidc"))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<OidcAuthenticationOptions>()
            .Bind(configuration.GetSection("Oidc:Authentication"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<IOidcProvider, GenericOidcProvider>();

        services.AddSingleton<IOidcProviderFactory, OidcProviderFactory>();
        services.AddSingleton<IOidcClaimsMapper, OidcClaimsMapper>();
        services.AddSingleton<IOidcTokenService, OidcTokenService>();

        return services;
    }

    public static IServiceCollection AddOidcIdentity(
        this IServiceCollection services,
        Action<OidcConfiguration> configureOptions)
    {
        services.AddSingleton<IValidateOptions<OidcConfiguration>, OidcConfigurationValidator>();
        services.AddOptions<OidcConfiguration>()
            .Configure(configureOptions)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<OidcAuthenticationOptions>()
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<IOidcProvider, GenericOidcProvider>();

        services.AddSingleton<IOidcProviderFactory, OidcProviderFactory>();
        services.AddSingleton<IOidcClaimsMapper, OidcClaimsMapper>();
        services.AddSingleton<IOidcTokenService, OidcTokenService>();

        return services;
    }

    public static IServiceCollection AddOidcAuthentication(
        this IServiceCollection services,
        Action<OidcAuthenticationOptions> configureOptions)
    {
        services.AddOptions<OidcAuthenticationOptions>()
            .Configure(configureOptions)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<OidcAuthenticationMiddleware>();

        return services;
    }

    public static IServiceCollection AddOidcAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<OidcAuthenticationOptions>()
            .Bind(configuration.GetSection("Oidc:Authentication"))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<OidcAuthenticationMiddleware>();

        return services;
    }

    public static IApplicationBuilder UseOidcAuthentication(
        this IApplicationBuilder app)
    {
        return app.Use(next =>
        {

            return new RequestDelegate(async context =>
            {
                var middleware = context.RequestServices
                    .GetRequiredService<OidcAuthenticationMiddleware>()
                    .Configure(next);
                await middleware.InvokeAsync(context).ConfigureAwait(false);
            });
        });
    }
}
