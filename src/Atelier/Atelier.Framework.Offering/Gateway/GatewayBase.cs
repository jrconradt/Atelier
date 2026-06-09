using System.Security.Claims;
using Atelier.Framework.Context;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;

namespace Atelier.Framework.Offering;

public abstract class GatewayBase : OfferingBase
{
    protected override void OnStart()
    {
    }

    protected override void OnStop()
    {
    }

    protected virtual Task<Outcome> AuthorizeAsync()
    {
        Observe(LogLevel.Warning, values: [("Reason", "Authorization not configured for this gateway")]);
        return Task.FromResult(Outcome.Failure());
    }

    protected void ApplyPrincipal(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")?.Value;

        var tenantId = principal.FindFirst("tid")?.Value
            ?? principal.FindFirst("tenant")?.Value;

        var authorization = AuthorizationContext.Create(userId, tenantId);

        foreach (var claim in principal.Claims)
        {
            authorization.AddClaim(claim.Type, claim.Value);
        }

        foreach (var role in principal.FindAll(ClaimTypes.Role))
        {
            authorization.AddRole(role.Value);
        }

        Context.Authorization = authorization;
    }

    protected async Task<Outcome<TResult>> ForwardAsync<TResult>(
        string operation,
        Func<Outcome<TResult>, Outcome<TResult>> validateResponse,
        Func<Task<Outcome<TResult>>> forward)
    {
        var authorization = await AuthorizeAsync().ConfigureAwait(false);
        if (!authorization.IsSuccess)
        {
            return Outcome<TResult>.Failure();
        }

        Observe(LogLevel.Debug, values: [("Operation", operation)]);

        var response = await ForwardInContextAsync(operation, forward).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            return response;
        }

        return validateResponse(response);
    }

    protected async Task<Outcome> ForwardAsync(
        string operation,
        Func<Task<Outcome>> forward)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(forward);

        var authorization = await AuthorizeAsync().ConfigureAwait(false);
        if (!authorization.IsSuccess)
        {
            return authorization;
        }

        Observe(LogLevel.Debug, values: [("Operation", operation)]);

        return await ForwardInContextAsync(operation, forward).ConfigureAwait(false);
    }

    private async Task<T> ForwardInContextAsync<T>(
        string operation,
        Func<Task<T>> forward)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(forward);

        var parent = ContextAccessor.Current;
        var authorization = parent.Authorization;
        var child = parent.CreateChild(operation, parent.Scope);

        if (authorization is not null)
        {
            child = child.WithAuthorization(authorization);
        }

        ContextAccessor.SetCurrent(child);
        try
        {
            return await forward().ConfigureAwait(false);
        }
        finally
        {
            ContextAccessor.SetCurrent(parent);
        }
    }
}
