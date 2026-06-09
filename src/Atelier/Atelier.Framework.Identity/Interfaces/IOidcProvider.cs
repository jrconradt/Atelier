using Atelier.Framework.Identity.Models;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Observability;
using Atelier.Framework.Attributes;

namespace Atelier.Framework.Identity.Interfaces;

public interface IOidcProvider
{
    string ProviderName { get; }

    string Authority { get; }

    bool IsConfigured { get; }

    Task<Outcome<OidcTokenResult>> AuthenticateAsync(OidcAuthorizationCodeExchange exchange, CancellationToken cancellationToken = default);

    Task<Outcome<OidcTokenResult>> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);

    Task<Outcome<OidcUserInfo>> GetUserInfoAsync(string accessToken, CancellationToken cancellationToken = default);

    Task<Outcome> ValidateTokenAsync(string token, CancellationToken cancellationToken = default);

    Task<Outcome<string>> GetAuthorizationUrlAsync(string? state = null, string? nonce = null, string? codeChallenge = null, string? codeChallengeMethod = null, CancellationToken cancellationToken = default);

    Task<Outcome> RevokeTokenAsync(string token, CancellationToken cancellationToken = default);

    Task<Outcome> LogoutAsync(string? idToken = null, CancellationToken cancellationToken = default);
}
