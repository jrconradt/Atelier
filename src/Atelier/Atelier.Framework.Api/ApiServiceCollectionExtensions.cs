
using Atelier.Framework.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Atelier.Framework.Infrastructure.Extensions;

public static class ApiServiceCollectionExtensions
{
    public static IServiceCollection ConfigureApiServices(
        this IServiceCollection services,
        ApiConfiguration configuration)
    {
        if (configuration == null)
        {
            return services;
        }

        if (configuration.Cors != null)
        {
            bool hasExplicitOrigins = configuration.Cors.AllowedOrigins != null
                && configuration.Cors.AllowedOrigins.Length > 0;

            if (configuration.Cors.AllowCredentials && !hasExplicitOrigins)
            {
                throw new InvalidOperationException(
                    "CORS AllowCredentials requires explicit AllowedOrigins; wildcard origins cannot be combined with credentials.");
            }

            if (configuration.Cors.AllowAnyOrigin && hasExplicitOrigins)
            {
                throw new InvalidOperationException(
                    "CORS AllowAnyOrigin cannot be combined with explicit AllowedOrigins.");
            }

            services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    if (hasExplicitOrigins)
                    {
                        policy.WithOrigins(configuration.Cors.AllowedOrigins!);
                    }
                    else if (configuration.Cors.AllowAnyOrigin)
                    {
                        policy.AllowAnyOrigin();
                    }

                    if (configuration.Cors.AllowedMethods != null && configuration.Cors.AllowedMethods.Length > 0)
                    {
                        policy.WithMethods(configuration.Cors.AllowedMethods);
                    }
                    else
                    {
                        policy.AllowAnyMethod();
                    }

                    if (configuration.Cors.AllowedHeaders != null && configuration.Cors.AllowedHeaders.Length > 0)
                    {
                        policy.WithHeaders(configuration.Cors.AllowedHeaders);
                    }
                    else
                    {
                        policy.AllowAnyHeader();
                    }

                    if (configuration.Cors.AllowCredentials)
                    {
                        policy.AllowCredentials();
                    }
                });
            });
        }

        return services;
    }
}

[Contract("ApiConfiguration", Version = "1.0", Namespace = "Framework.Infrastructure.Extensions")]
public partial class ApiConfiguration
{
    public AuthenticationConfiguration? Authentication { get; set; }
    public CorsConfiguration? Cors { get; set; }
    public RateLimitConfiguration? RateLimit { get; set; }
}

[Contract("AuthenticationConfiguration", Version = "1.0", Namespace = "Framework.Infrastructure.Extensions")]
public partial class AuthenticationConfiguration
{
    public JwtConfiguration? Jwt { get; set; }
}

[Contract("JwtConfiguration", Version = "1.0", Namespace = "Framework.Infrastructure.Extensions")]
public partial class JwtConfiguration
{
    public string? Issuer { get; set; }
    public string? Audience { get; set; }
    public string? SecretKeyReference { get; set; }
    public int ExpirationMinutes { get; set; } = 60;
}

[Contract("CorsConfiguration", Version = "1.0", Namespace = "Framework.Infrastructure.Extensions")]
public partial class CorsConfiguration
{
    public string[]? AllowedOrigins { get; set; }
    public string[]? AllowedMethods { get; set; }
    public string[]? AllowedHeaders { get; set; }
    public bool AllowCredentials { get; set; } = false;
    public bool AllowAnyOrigin { get; set; } = false;
}

[Contract("RateLimitConfiguration", Version = "1.0", Namespace = "Framework.Infrastructure.Extensions")]
public partial class RateLimitConfiguration
{
    public bool Enabled { get; set; } = false;
    public int RequestsPerMinute { get; set; } = 60;
    public int RequestsPerHour { get; set; } = 1000;
}
