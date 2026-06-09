using System.Security.Claims;
using Atelier.Framework.Identity.Interfaces;
using Atelier.Framework.Identity.Models;
using Atelier.Framework.Identity.Services;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Testing;
using ILogger = Atelier.Framework.Observability.ILogger;

namespace Atelier.Framework.Identity.Tests;

public static class OidcIdentityVerifierTests
{
    private const string TARGET = "global::Atelier.Framework.Identity.Services.OidcIdentityVerifier";

    private static OidcIdentityVerifier Verifier(bool valid)
        => new(new StubTokenService(valid),
               AutoMockProvider.For<ILogger>());

    private sealed class StubTokenService : IOidcTokenService
    {
        private readonly bool _valid;

        public StubTokenService(bool valid)
        {
            _valid = valid;
        }

        public Task<Outcome<ClaimsPrincipal>> ValidateTokenAsync(string token, string? providerName = null, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(token);

            if (!_valid)
            {
                return Task.FromResult(Outcome<ClaimsPrincipal>.Failure());
            }

            var identity = new ClaimsIdentity(new[]
            {
                new Claim("sub", "user-1"),
                new Claim("tid", "tenant-1"),
                new Claim("role", "admin")
            }, "test");
            return Task.FromResult(Outcome<ClaimsPrincipal>.Success(new ClaimsPrincipal(identity)));
        }

        public Task<Outcome<OidcUserInfo>> ExtractUserInfoAsync(string token, string? providerName = null, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(token);
            return Task.FromResult(Outcome<OidcUserInfo>.Failure());
        }

        public Task<Outcome<OidcTokenResult>> RefreshTokenAsync(string refreshToken, string? providerName = null, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
            return Task.FromResult(Outcome<OidcTokenResult>.Failure());
        }

        public Task<Outcome> IsTokenValidAsync(string token, string? providerName = null, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(token);
            return Task.FromResult(_valid ? Outcome.Success() : Outcome.Failure());
        }

        public Task<Outcome> RevokeTokenAsync(string token, string? providerName = null, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(token);
            return Task.FromResult(Outcome.Success());
        }

        public Task<Outcome<Dictionary<string, object>>> ExtractClaimsAsync(string token, string? providerName = null, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(token);
            return Task.FromResult(Outcome<Dictionary<string, object>>.Failure());
        }
    }

    private static void IsTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    [GeneratedTest("identity.verifier.valid-token-yields-verified-context", TARGET)]
    public static async Task ValidTokenYieldsVerifiedContext()
    {
        var verifier = Verifier(valid: true);

        var result = await verifier.VerifyAsync("a.valid.token").ConfigureAwait(false);

        IsTrue(result.IsSuccess, "A valid token should verify");
        IsTrue(result.Data!.IsVerified, "A verified token must produce a verified authorization context");
        IsTrue(result.Data.IsValid(), "A verified, unexpired context must be valid");
        IsTrue(result.Data.UserId == "user-1", $"UserId should map from the 'sub' claim, got {result.Data.UserId}");
        IsTrue(result.Data.HasClaim("role"), "Claims from the principal should be carried into the authorization context");
    }

    [GeneratedTest("identity.verifier.role-claim-yields-role", TARGET)]
    public static async Task RoleClaimYieldsRole()
    {
        var verifier = Verifier(valid: true);

        var result = await verifier.VerifyAsync("a.valid.token").ConfigureAwait(false);

        IsTrue(result.IsSuccess, "A valid token should verify");
        IsTrue(result.Data!.HasRole("admin"), "A 'role' claim must be carried into the authorization context as a role");
    }

    [GeneratedTest("identity.verifier.invalid-token-is-rejected", TARGET)]
    public static async Task InvalidTokenIsRejected()
    {
        var verifier = Verifier(valid: false);

        var result = await verifier.VerifyAsync("a.bad.token").ConfigureAwait(false);

        IsTrue(!result.IsSuccess, "An invalid token must not verify");
    }
}
