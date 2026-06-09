using Atelier.Framework.Primitives;
using System.Security.Claims;
using Atelier.Framework.Context;
using Atelier.Framework.Identity.Interfaces;
using Atelier.Framework.Attributes;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;

namespace Atelier.Framework.Identity.Services;

[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
[Infrastructure(InfrastructureLifetime.Singleton)]
public partial class OidcIdentityVerifier : IIdentityVerifier, IAtelier
{
    [Requisite] protected readonly IOidcTokenService _tokenService = null!;

    public async Task<Outcome<AuthorizationContext>> VerifyAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            Observe(
                LogLevel.Warning,
                values: [("Event", "IdentityVerificationFailed"), ("Reason", "Token is required")]);
            return Outcome<AuthorizationContext>.Failure();
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome<AuthorizationContext>.Failure();
        }

        var validation = await _tokenService.ValidateTokenAsync(token, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!validation.IsSuccess
            || validation.Data == null)
        {
            Observe(
                LogLevel.Warning,
                values: [("Event", "IdentityVerificationFailed"), ("Reason", "Token validation failed")]);
            return Outcome<AuthorizationContext>.Failure();
        }

        var principal = validation.Data;
        var userId = principal.FindFirst("sub")?.Value ?? principal.Identity?.Name;
        var tenantId = principal.FindFirst("tid")?.Value;

        var authorization = AuthorizationContext.Create(userId: userId, tenantId: tenantId, isVerified: true);
        foreach (var claim in principal.Claims)
        {
            _ = authorization.AddClaim(claim.Type, claim.Value);

            if (IsRoleClaim(claim.Type))
            {
                _ = authorization.AddRole(claim.Value, true);
            }
        }

        return Outcome<AuthorizationContext>.Success(authorization);
    }

    private static bool IsRoleClaim(string claimType)
    {
        return claimType == ClaimTypes.Role
            || claimType == "roles"
            || claimType == "role";
    }
}
