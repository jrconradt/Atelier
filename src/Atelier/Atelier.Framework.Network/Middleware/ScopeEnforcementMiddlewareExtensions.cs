using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Atelier.Framework.Network.Middleware;

public static class ScopeEnforcementMiddlewareExtensions
{
    public static IApplicationBuilder UseScopeEnforcement(this IApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Use(next =>
        {
            var middleware = builder.ApplicationServices
                .GetRequiredService<ScopeEnforcementMiddleware>()
                .Configure(next);
            return new RequestDelegate(middleware.InvokeAsync);
        });
    }
}
