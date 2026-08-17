using System.Security.Claims;
using Atelier.Framework.Context;
using Atelier.Framework.Identity.Interfaces;
using Atelier.Framework.Identity.Models;
using Atelier.Framework.Attributes;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Atelier.Framework.Identity.Middleware;

[ContractAttribute("OidcAuthenticationMiddleware", Version = "1.0")]
public partial class OidcAuthenticationMiddleware : IAtelier
{
    [Requisite] private readonly IOptions<OidcAuthenticationOptions> _optionsAccessor = null!;
    [Requisite] private readonly IOidcTokenService _tokenService = null!;
    [Requisite] private readonly IOidcProviderFactory _providerFactory = null!;

    private RequestDelegate? _next;

    private OidcAuthenticationOptions _options => _optionsAccessor.Value;

    public OidcAuthenticationMiddleware Configure(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        return this;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var contextManager = context.RequestServices.GetRequiredService<ContextManager>();
        if (_next is null)
        {
            throw new InvalidOperationException(
                $"{nameof(OidcAuthenticationMiddleware)} was not configured with a RequestDelegate. Call Configure(next) before InvokeAsync.");
        }

        if (ShouldSkipAuthentication(context))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var token = ExtractTokenFromRequest(context);
        if (string.IsNullOrEmpty(token))
        {
            if (_options.RequireAuthentication)
            {
                Observe(LogLevel.Warning, values: [("Event", "AuthenticationMissingToken"), ("CorrelationId", context.TraceIdentifier), ("Path", context.Request.Path.Value ?? string.Empty), ("Method", context.Request.Method)]);

                context.Response.StatusCode = 401;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"error\": \"Authentication required\", \"code\": \"UNAUTHORIZED\"}").ConfigureAwait(false);
                return;
            }

            await _next(context).ConfigureAwait(false);
            return;
        }

        var authContextResult = await CreateAuthorizationContextFromTokenAsync(token, context.RequestAborted).ConfigureAwait(false);
        if (!authContextResult.IsSuccess || authContextResult.Data == null)
        {
            Observe(LogLevel.Warning, values: [("Event", "AuthenticationTokenRejected"), ("CorrelationId", context.TraceIdentifier), ("Path", context.Request.Path.Value ?? string.Empty), ("Method", context.Request.Method)]);

            if (_options.RequireAuthentication)
            {
                context.Response.StatusCode = 401;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"error\": \"Authentication failed\", \"code\": \"UNAUTHORIZED\"}").ConfigureAwait(false);
                return;
            }

            await _next(context).ConfigureAwait(false);
            return;
        }

        IContext requestContext;
        try
        {
            requestContext = await contextManager.CreateContextAsync(
                name: $"{context.Request.Method} {context.Request.Path}",
                scope: ContextScope.Service,
                serviceId: _options.ServiceId,
                domainId: _options.DomainId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Observe(LogLevel.Error,
                    ex, values: [("Path", context.Request.Path.Value ?? string.Empty), ("Method", context.Request.Method)]);

            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\": \"Failed to establish request context\", \"code\": \"CONTEXT_CREATION_FAILED\"}").ConfigureAwait(false);
            return;
        }

        requestContext.WithAuthorization(authContextResult.Data);

        if (!string.IsNullOrEmpty(context.TraceIdentifier))
        {
            requestContext.TraceId = context.TraceIdentifier;
        }

        context.Items["AtelierContext"] = requestContext;

        var authorization = authContextResult.Data;
        Observe(LogLevel.Information, values: [("Event", "AuthenticationSucceeded"), ("CorrelationId", context.TraceIdentifier), ("IdentityId", authorization.UserId ?? "unknown"), ("TenantId", authorization.TenantId ?? string.Empty), ("SessionId", authorization.SessionId ?? string.Empty), ("Path", context.Request.Path.Value ?? string.Empty), ("Method", context.Request.Method)]);

        try
        {
            await _next(context).ConfigureAwait(false);
        }
        finally
        {
            await contextManager.FinalizeContextAsync(requestContext, ContextStatus.Success).ConfigureAwait(false);
        }
    }

    private bool ShouldSkipAuthentication(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!_options.EnableAuthentication)
        {
            return true;
        }

        var path = context.Request.Path.Value;
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        return _options.ExcludedPaths.Any(excludedPath =>
            string.Equals(path, excludedPath, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith($"{excludedPath}/", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<Outcome<Atelier.Framework.Context.AuthorizationContext>> CreateAuthorizationContextFromTokenAsync(
        string token,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.ProviderName))
        {
            Observe(
                LogLevel.Warning,
                values: [("Event", "AuthorizationContextCreationFailed"), ("Reason", "Provider name not configured")]);
            return Outcome<Atelier.Framework.Context.AuthorizationContext>.Failure();
        }


        try
        {
            var providerResult = await _providerFactory.GetProviderAsync(_options.ProviderName, cancellationToken).ConfigureAwait(false);
            if (!providerResult.IsSuccess || providerResult.Data == null)
            {
                return Outcome<Atelier.Framework.Context.AuthorizationContext>.Failure();
            }

            var provider = providerResult.Data;
            var validationResult = await provider.ValidateTokenAsync(token, cancellationToken).ConfigureAwait(false);
            if (!validationResult.IsSuccess)
            {
                return Outcome<Atelier.Framework.Context.AuthorizationContext>.Failure();
            }

            var claimsResult = await _tokenService.ExtractClaimsAsync(token, _options.ProviderName, cancellationToken).ConfigureAwait(false);
            if (!claimsResult.IsSuccess || claimsResult.Data == null)
            {
                return Outcome<Atelier.Framework.Context.AuthorizationContext>.Failure();
            }

            var claims = claimsResult.Data;

            var identityId = claims.GetValueOrDefault("sub")?.ToString() ?? claims.GetValueOrDefault("oid")?.ToString();
            if (string.IsNullOrEmpty(identityId))
            {
                Observe(
                    LogLevel.Warning,
                    values: [("Event", "AuthorizationContextCreationFailed"), ("Reason", "Token does not carry a subject claim"), ("Provider", _options.ProviderName)]);
                return Outcome<Atelier.Framework.Context.AuthorizationContext>.Failure();
            }

            var tenantId = claims.GetValueOrDefault("tenant_id")?.ToString() ?? claims.GetValueOrDefault("tid")?.ToString();
            var sessionId = claims.GetValueOrDefault("sid")?.ToString() ?? claims.GetValueOrDefault("session_id")?.ToString();

            var authContext = Atelier.Framework.Context.AuthorizationContext.Create(
                identityId,
                tenantId,
                sessionId);

            var scopes = claims
                .Where(c => c.Key == "scope")
                .SelectMany(c => (c.Value?.ToString() ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .ToArray();

            foreach (var scope in scopes)
            {
                var result = authContext.AddPermission(scope, true);
                if (result.IsSuccess)
                {
                    authContext = result.Data!;
                }
            }

            var roles = claims
                .Where(c => c.Key == "roles" || c.Key == "role")
                .SelectMany(c => ExtractRoleValues(c.Value))
                .ToArray();

            foreach (var role in roles)
            {
                var result = authContext.AddRole(role, true);
                if (result.IsSuccess)
                {
                    authContext = result.Data!;
                }
            }

            foreach (var claim in claims.Where(c => c.Key != "scope"))
            {
                var result = authContext.AddClaim(claim.Key, claim.Value?.ToString() ?? string.Empty);
                if (result.IsSuccess)
                {
                    authContext = result.Data!;
                }
            }

            if (claims.TryGetValue("exp", out var expValue)
                && long.TryParse(expValue?.ToString(), out var exp))
            {
                authContext.ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime;
            }
            else if (_options.RequireAuthentication)
            {
                Observe(
                    LogLevel.Warning,
                    values: [("Event", "AuthorizationContextCreationFailed"), ("Reason", "Token does not carry a valid expiration claim"), ("Provider", _options.ProviderName)]);
                return Outcome<Atelier.Framework.Context.AuthorizationContext>.Failure();
            }

            Observe(LogLevel.Debug, values: [("Scopes", scopes.Length)]);

            return Outcome<Atelier.Framework.Context.AuthorizationContext>.Success(authContext);
        }
        catch (Exception ex)
        {
            Observe(
                LogLevel.Error,
                ex,
                values: [("Event", "AuthorizationContextCreationFailed"), ("Reason", "Failed to create authorization context"), ("Provider", _options.ProviderName)]);

            return Outcome<Atelier.Framework.Context.AuthorizationContext>.Failure();
        }
    }

    private static IEnumerable<string> ExtractRoleValues(object? value)
    {
        if (value is null)
        {
            return [];
        }

        if (value is string single)
        {
            return string.IsNullOrWhiteSpace(single) ? [] : [single];
        }

        if (value is IEnumerable<string> many)
        {
            return many.Where(r => !string.IsNullOrWhiteSpace(r));
        }

        if (value is System.Collections.IEnumerable sequence)
        {
            var collected = new List<string>();
            foreach (var item in sequence)
            {
                var text = item?.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    collected.Add(text);
                }
            }

            return collected;
        }

        var fallback = value.ToString();
        return string.IsNullOrWhiteSpace(fallback) ? [] : [fallback];
    }

    private string? ExtractTokenFromRequest(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader))
        {
            return null;
        }

        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return authHeader.Substring("Bearer ".Length).Trim();
    }
}

[ContractAttribute("OidcAuthenticationOptions", Version = "1.0")]
public class OidcAuthenticationOptions
{
    public bool EnableAuthentication { get; set; } = true;
    public bool RequireAuthentication { get; set; } = true;
    public string? ProviderName { get; set; }
    public string? ServiceId { get; set; }
    public string? DomainId { get; set; }
    public List<string> ExcludedPaths { get; set; } = ["/health", "/metrics", "/swagger"];
}
