using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Atelier.Framework.Context;
using Atelier.Framework.Infrastructure.Operation;
using Atelier.Framework.Attributes;

namespace Atelier.Framework.Network.Middleware;

public static class ContextExtractionMiddlewareExtensions
{
    public static IApplicationBuilder UseContextExtraction(this IApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Use(next =>
        {
            var verifier = builder.ApplicationServices.GetService<IIdentityVerifier>();
            var middleware = builder.ApplicationServices
                .GetRequiredService<ContextExtractionMiddleware>()
                .Configure(next, verifier);
            return new RequestDelegate(middleware.InvokeAsync);
        });
    }
}
