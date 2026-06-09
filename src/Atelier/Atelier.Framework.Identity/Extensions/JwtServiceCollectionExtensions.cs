using Atelier.Framework.Identity.Configuration;
using Atelier.Framework.Identity.Interfaces;
using Atelier.Framework.Identity.Services;
using Atelier.Framework.Attributes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Atelier.Framework.Identity.Extensions;

[ContractAttribute("JwtServiceCollectionExtensions", Version = "1.0")]
public static class JwtServiceCollectionExtensions
{
    public static IServiceCollection AddJwtIdentity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<JwtAuthenticationOptions>()
            .Bind(configuration.GetSection("Jwt"))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        return RegisterJwtServices(services);
    }

    public static IServiceCollection AddJwtIdentity(
        this IServiceCollection services,
        Action<JwtAuthenticationOptions> configureOptions)
    {
        services.AddOptions<JwtAuthenticationOptions>()
            .Configure(configureOptions)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        return RegisterJwtServices(services);
    }

    public static AuthenticationBuilder AddJwtBearerAuthentication(
        this IServiceCollection services,
        Action<JwtAuthenticationOptions> configureOptions)
    {
        services.AddOptions<JwtAuthenticationOptions>()
            .Configure(configureOptions)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        RegisterJwtServices(services);

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IJwtTokenValidator>((bearer, validator) =>
            {
                bearer.TokenValidationParameters = validator.CreateValidationParameters();
            });

        return services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();
    }

    private static IServiceCollection RegisterJwtServices(IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IValidateOptions<JwtAuthenticationOptions>, JwtAuthenticationOptionsValidator>();
        services.AddSingleton<IJwtTokenValidator, JwtTokenValidator>();
        services.AddSingleton<IJwtTokenIssuer, JwtTokenIssuer>();
        return services;
    }
}
