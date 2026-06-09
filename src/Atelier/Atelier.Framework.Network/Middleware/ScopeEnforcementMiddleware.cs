using Atelier.Framework.Primitives;
using System.Runtime.CompilerServices;
using Atelier.Framework.Attributes;
using Atelier.Framework.Context;
using Atelier.Framework.Network.Enforcement;
using Atelier.Framework.Observability;
using Atelier.Framework.Requisitions;
using Microsoft.AspNetCore.Http;

namespace Atelier.Framework.Network.Middleware;

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class ScopeEnforcementMiddleware : IAtelier
{
    [Requisite] private readonly IContextAccessor _contextAccessor = null!;

    private readonly StrongBox<RequestDelegate?> _next = new(null);

    public ScopeEnforcementMiddleware Configure(RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(next);

        _next.Value = next;
        return this;
    }

    public async Task InvokeAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (_next.Value is null)
        {
            Observe(LogLevel.Error, values: [("Error", $"{nameof(ScopeEnforcementMiddleware)} was not configured with a RequestDelegate. Call Configure(next) before InvokeAsync.")]);
            return;
        }

        var operation = httpContext.GetEndpoint()?.Metadata.GetMetadata<ScopeEnforcedOperation>();
        if (operation is null)
        {
            await _next.Value(httpContext).ConfigureAwait(false);
            return;
        }

        var authorization = _contextAccessor.Current?.Authorization;

        var requiredScopes = ScopeRequirementResolver.ResolveRequiredScopes(operation.Operation);
        if (!ScopeAuthorizationEvaluator.IsAuthorized(authorization, requiredScopes))
        {
            Deny(httpContext, authorization, operation, "MissingScope");
            return;
        }

        if (ScopeRequirementResolver.TryResolveAllowSelf(operation.Operation, out var identityParameterName))
        {
            var identityValue = httpContext.Request.RouteValues.TryGetValue(identityParameterName, out var routeValue)
                ? routeValue?.ToString()
                : null;

            if (!ScopeAuthorizationEvaluator.IsSelf(authorization, identityValue))
            {
                Deny(httpContext, authorization, operation, "NotSelf");
                return;
            }
        }

        await _next.Value(httpContext).ConfigureAwait(false);
    }

    private void Deny(HttpContext httpContext,
                      AuthorizationContext? authorization,
                      ScopeEnforcedOperation operation,
                      string reason)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(reason);

        Observe(
            LogLevel.Warning,
            values: [("Event", "AuthorizationDenied"), ("Decision", "Forbidden"), ("Reason", reason), ("Subject", authorization?.UserId ?? "anonymous"), ("TargetType", operation.Operation.DeclaringType?.FullName ?? "unknown"), ("Method", operation.Operation.Name)]);
        httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
    }
}
