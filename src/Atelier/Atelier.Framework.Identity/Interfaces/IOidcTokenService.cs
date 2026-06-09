using Atelier.Framework.Identity.Models;
using Atelier.Framework.Outcomes;
using System.Security.Claims;
using Atelier.Framework.Observability;
using Atelier.Framework.Attributes;

namespace Atelier.Framework.Identity.Interfaces;

public interface IOidcTokenService
{
    Task<Outcome<ClaimsPrincipal>> ValidateTokenAsync(string token, string? providerName = null, CancellationToken cancellationToken = default);

    Task<Outcome<OidcUserInfo>> ExtractUserInfoAsync(string token, string? providerName = null, CancellationToken cancellationToken = default);

    Task<Outcome<OidcTokenResult>> RefreshTokenAsync(string refreshToken, string? providerName = null, CancellationToken cancellationToken = default);

    Task<Outcome> IsTokenValidAsync(string token, string? providerName = null, CancellationToken cancellationToken = default);

    Task<Outcome> RevokeTokenAsync(string token, string? providerName = null, CancellationToken cancellationToken = default);

    Task<Outcome<Dictionary<string, object>>> ExtractClaimsAsync(string token, string? providerName = null, CancellationToken cancellationToken = default);
}
