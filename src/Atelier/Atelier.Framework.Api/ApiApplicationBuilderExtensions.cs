using Microsoft.AspNetCore.Builder;

namespace Atelier.Framework.Infrastructure.Extensions;

public static class ApiApplicationBuilderExtensions
{
    public static IApplicationBuilder UseApiMiddleware(
        this IApplicationBuilder app,
        ApiConfiguration configuration)
    {
        if (configuration == null)
        {
            return app;
        }

        if (configuration.Cors != null)
        {
            app.UseCors();
        }

        return app;
    }
}
