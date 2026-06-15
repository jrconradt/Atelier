using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Atelier.Framework.Identity.Configuration;
using Atelier.Framework.Identity.Services;
using Atelier.Framework.Resilience;
using Atelier.Framework.Testing;
using ILogger = Atelier.Framework.Observability.ILogger;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Atelier.Framework.Identity.Tests;

public sealed class GenericOidcProviderIntegrationTests
{
    private const string AUTHORITY = "http://oidc.localhost";
    private const string CLIENT_ID = "atelier-client";
    private const string SIGNING_KEY_ID = "test-key-1";

    private sealed class EmulatedOidcHandler : HttpMessageHandler
    {
        private readonly string _discoveryJson;
        private readonly string _jwksJson;
        private readonly string _tokenJson;

        public EmulatedOidcHandler(
            string discoveryJson,
            string jwksJson,
            string tokenJson)
        {
            _discoveryJson = discoveryJson;
            _jwksJson = jwksJson;
            _tokenJson = tokenJson;
        }

        public int DiscoveryHits { get; private set; }
        public int JwksHits { get; private set; }
        public int TokenHits { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("/.well-known/openid-configuration", StringComparison.Ordinal))
            {
                DiscoveryHits++;
                return Task.FromResult(Json(_discoveryJson));
            }

            if (path.EndsWith("/jwks", StringComparison.Ordinal))
            {
                JwksHits++;
                return Task.FromResult(Json(_jwksJson));
            }

            if (path.EndsWith("/token", StringComparison.Ordinal))
            {
                TokenHits++;
                return Task.FromResult(Json(_tokenJson));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(string body)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class EmulatedProvider
    {
        public required GenericOidcProvider Provider { get; init; }
        public required EmulatedOidcHandler Handler { get; init; }
        public required RsaSecurityKey SigningKey { get; init; }
    }

    private static EmulatedProvider BuildProvider(string tokenJson)
    {
        var rsa = RSA.Create(2048);
        var signingKey = new RsaSecurityKey(rsa)
        {
            KeyId = SIGNING_KEY_ID,
        };

        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(signingKey);
        jwk.Use = "sig";
        jwk.Alg = SecurityAlgorithms.RsaSha256;
        var jwksJson = JsonSerializer.Serialize(new JsonWebKeySet { Keys = { jwk } });

        var discoveryJson = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["issuer"] = AUTHORITY,
            ["jwks_uri"] = $"{AUTHORITY}/jwks",
            ["token_endpoint"] = $"{AUTHORITY}/token",
            ["authorization_endpoint"] = $"{AUTHORITY}/authorize",
            ["userinfo_endpoint"] = $"{AUTHORITY}/userinfo",
        });

        var handler = new EmulatedOidcHandler(discoveryJson,
                                              jwksJson,
                                              tokenJson);

        var httpClient = new HttpClient(handler);

        var config = new OidcProviderConfiguration
        {
            Authority = AUTHORITY,
            ClientId = CLIENT_ID,
            ClientSecret = "secret",
            RequireHttps = false,
            AllowInsecureValidation = true,
        };

        var oidcConfiguration = new OidcConfiguration
        {
            DefaultProvider = "test",
            Providers =
            {
                ["test"] = config,
            },
        };

        var claimsMapper = new OidcClaimsMapper(oidcConfiguration);
        var resilience = new ResiliencePipelineFactory(new ConfigurationBuilder().Build(),
                                                       AutoMockProvider.For<ILogger>());

        var provider = new GenericOidcProvider(httpClient,
                                               claimsMapper,
                                               resilience,
                                               AutoMockProvider.For<ILogger>())
            .Configure("test", config);

        return new EmulatedProvider
        {
            Provider = provider,
            Handler = handler,
            SigningKey = signingKey,
        };
    }

    private static string SignToken(
        RsaSecurityKey signingKey,
        DateTime expires)
    {
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256);
        var notBefore = expires.AddMinutes(-1) < DateTime.UtcNow.AddMinutes(-1)
            ? expires.AddMinutes(-1)
            : DateTime.UtcNow.AddMinutes(-1);
        var token = new JwtSecurityToken(issuer: AUTHORITY,
                                         audience: CLIENT_ID,
                                         claims: new[]
                                         {
                                             new Claim(JwtRegisteredClaimNames.Sub, "user-1"),
                                         },
                                         notBefore: notBefore,
                                         expires: expires,
                                         signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public async Task ValidateTokenAsync_AcceptsTokenSignedByDiscoveredJwksKey()
    {
        var emulated = BuildProvider("{}");
        var token = SignToken(emulated.SigningKey, DateTime.UtcNow.AddHours(1));

        var result = await emulated.Provider.ValidateTokenAsync(token);

        Assert.True(result.IsSuccess);
        Assert.True(emulated.Handler.DiscoveryHits > 0);
        Assert.True(emulated.Handler.JwksHits > 0);
    }

    [Fact]
    public async Task ValidateTokenAsync_RejectsExpiredToken()
    {
        var emulated = BuildProvider("{}");
        var token = SignToken(emulated.SigningKey, DateTime.UtcNow.AddMinutes(-30));

        var result = await emulated.Provider.ValidateTokenAsync(token);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ValidateTokenAsync_RejectsTokenSignedByUnknownKey()
    {
        var emulated = BuildProvider("{}");
        var foreignKey = new RsaSecurityKey(RSA.Create(2048))
        {
            KeyId = "foreign-key",
        };
        var token = SignToken(foreignKey, DateTime.UtcNow.AddHours(1));

        var result = await emulated.Provider.ValidateTokenAsync(token);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task RefreshTokenAsync_ExchangesRefreshTokenThroughDiscoveredTokenEndpoint()
    {
        var tokenJson = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["access_token"] = "new-access-token",
            ["refresh_token"] = "new-refresh-token",
            ["token_type"] = "Bearer",
            ["expires_in"] = 3600,
        });
        var emulated = BuildProvider(tokenJson);

        var result = await emulated.Provider.RefreshTokenAsync("old-refresh-token");

        Assert.True(result.IsSuccess);
        Assert.Equal("new-access-token", result.Data!.AccessToken);
        Assert.Equal("new-refresh-token", result.Data.RefreshToken);
        Assert.True(emulated.Handler.TokenHits > 0);
    }
}
