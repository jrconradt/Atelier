using Atelier.Framework.Attributes;
using Atelier.Framework.Identity.Configuration;
using Atelier.Framework.Identity.Interfaces;
using Atelier.Framework.Identity.Models;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;
using Atelier.Framework.Resilience;
using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Text;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace Atelier.Framework.Identity.Services;

[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class GenericOidcProvider : IAtelier, IOidcProvider
{
    [Requisite] protected readonly HttpClient _httpClient = null!;
    [Requisite] protected readonly IOidcClaimsMapper _claimsMapper = null!;
    [Requisite] protected readonly ResiliencePipelineFactory _resilience = null!;

    private readonly OidcProviderState _state = new();

    protected OidcProviderConfiguration _config => _state.Config;
    protected string _providerName => _state.ProviderName;

    protected readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    protected readonly TimeSpan _discoveryCacheDuration = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, Lazy<Task<DiscoveryCacheEntry>>> _discoveryCache = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<SigningKeysCacheEntry>>> _signingKeysCache = new();

    private static readonly string[] AllowedSigningAlgorithms =
    {
        "RS256",
        "RS384",
        "RS512",
        "ES256",
        "ES384",
        "ES512",
        "PS256",
        "PS384",
        "PS512"
    };

    private static class OidcWireNames
    {
        public const string DISCOVERY_PATH = "/.well-known/openid-configuration";
        public const string BEARER = "Bearer";

        public const string TOKEN_ENDPOINT = "token_endpoint";
        public const string AUTHORIZATION_ENDPOINT = "authorization_endpoint";
        public const string USERINFO_ENDPOINT = "userinfo_endpoint";
        public const string JWKS_URI = "jwks_uri";
        public const string REVOCATION_ENDPOINT = "revocation_endpoint";
        public const string END_SESSION_ENDPOINT = "end_session_endpoint";

        public const string GRANT_TYPE = "grant_type";
        public const string CODE = "code";
        public const string CLIENT_ID = "client_id";
        public const string CLIENT_SECRET = "client_secret";
        public const string REDIRECT_URI = "redirect_uri";
        public const string CODE_VERIFIER = "code_verifier";
        public const string REFRESH_TOKEN = "refresh_token";

        public const string ACCESS_TOKEN = "access_token";
        public const string ID_TOKEN = "id_token";
        public const string TOKEN_TYPE = "token_type";
        public const string EXPIRES_IN = "expires_in";
        public const string REFRESH_EXPIRES_IN = "refresh_expires_in";
        public const string SCOPE = "scope";

        public const string AUTHORIZATION_CODE_GRANT = "authorization_code";
        public const string REFRESH_TOKEN_GRANT = "refresh_token";
    }

    private sealed class OidcProviderState
    {
        public OidcProviderConfiguration Config { get; set; } = new();
        public string ProviderName { get; set; } = string.Empty;
    }

    [Contract("DiscoveryCacheEntry", Version = "1.0")]
    private sealed class DiscoveryCacheEntry
    {
        public Outcome<Dictionary<string, object>> Result { get; init; }
        public DateTime FetchedAtUtc { get; init; }
        public bool IsCacheable { get; init; }
    }

    [Contract("SigningKeysCacheEntry", Version = "1.0")]
    private sealed class SigningKeysCacheEntry
    {
        public Outcome<IReadOnlyCollection<SecurityKey>> Result { get; init; }
        public DateTime FetchedAtUtc { get; init; }
        public bool IsCacheable { get; init; }
    }

    public string ProviderName => _providerName;
    public string Authority => _config.Authority;
    public bool IsConfigured => !string.IsNullOrEmpty(_config.Authority) &&
                                !string.IsNullOrEmpty(_config.ClientId) &&
                                !string.IsNullOrEmpty(_config.ClientSecret);



    protected GenericOidcProvider() { }

    public GenericOidcProvider Configure(
        string providerName,
        OidcProviderConfiguration config)
    {
        _state.ProviderName = providerName ?? throw new ArgumentNullException(nameof(providerName));
        _state.Config = config ?? throw new ArgumentNullException(nameof(config));
        return this;
    }

    public async Task<Outcome<OidcTokenResult>> AuthenticateAsync(
        OidcAuthorizationCodeExchange exchange,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        ArgumentException.ThrowIfNullOrWhiteSpace(exchange.AuthorizationCode);


        var stateCheck = VerifyState(exchange.ExpectedState, exchange.ReturnedState);
        if (!stateCheck.IsSuccess)
        {
            Observe(
                LogLevel.Warning,
                values: [("Event", "AuthenticationFailed"), ("Reason", "Authorization callback state does not match the issued state"), ("Provider", _providerName)]);
            return Outcome<OidcTokenResult>.Failure();
        }

        try
        {
            var tokenEndpoint = await GetTokenEndpointAsync(cancellationToken).ConfigureAwait(false);
            if (!tokenEndpoint.IsSuccess || tokenEndpoint.Data == null)
            {
                return Outcome<OidcTokenResult>.Failure();
            }

            var tokenRequest = new Dictionary<string, string>
            {
                [OidcWireNames.GRANT_TYPE] = OidcWireNames.AUTHORIZATION_CODE_GRANT,
                [OidcWireNames.CODE] = exchange.AuthorizationCode,
                [OidcWireNames.CLIENT_ID] = _config.ClientId,
                [OidcWireNames.CLIENT_SECRET] = _config.ClientSecret,
                [OidcWireNames.REDIRECT_URI] = _config.RedirectUri ?? string.Empty
            };

            if (!string.IsNullOrEmpty(exchange.CodeVerifier))
            {
                tokenRequest[OidcWireNames.CODE_VERIFIER] = exchange.CodeVerifier;
            }

            return await _resilience.ExecuteWithResilienceAsync(
                _resilience.HttpPipeline,
                async ct =>
                {
                    try
                    {
                        var response = await _httpClient.PostAsync(tokenEndpoint.Data,
                                                                   new FormUrlEncodedContent(tokenRequest), ct).ConfigureAwait(false);

                        if (!response.IsSuccessStatusCode)
                        {
                            Observe(
                                LogLevel.Warning,
                                values: [("Event", "TokenExchangeFailed"), ("Reason", "Token endpoint returned non-success status"), ("StatusCode", response.StatusCode), ("Provider", _providerName)]);
                            return Outcome<OidcTokenResult>.Failure();
                        }

                        var tokenResponse = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(_jsonOptions, ct).ConfigureAwait(false);
                        if (tokenResponse == null)
                        {
                            Observe(
                                LogLevel.Warning,
                                values: [("Event", "TokenExchangeFailed"), ("Reason", "Failed to deserialize token response"), ("Provider", _providerName)]);
                            return Outcome<OidcTokenResult>.Failure();
                        }

                        var tokenResult = MapTokenResponse(tokenResponse);

                        var idTokenCheck = await ValidateIdTokenAsync(tokenResult.IdToken,
                                                                      tokenResult.AccessToken,
                                                                      exchange.ExpectedNonce,
                                                                      ct).ConfigureAwait(false);
                        if (!idTokenCheck.IsSuccess)
                        {
                            return Outcome<OidcTokenResult>.Failure();
                        }

                        return Outcome<OidcTokenResult>.Success(tokenResult);
                    }
                    catch (HttpRequestException ex)
                    {
                        Observe(
                            LogLevel.Warning,
                            ex,
                            values: [("Event", "TokenExchangeFailed"), ("Reason", "Token exchange connection failed"), ("Provider", _providerName)]);
                        return Outcome<OidcTokenResult>.Failure();
                    }
                },
                "Oidc.AuthenticateAsync",
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Observe(
                LogLevel.Error,
                ex,
                values: [("Event", "AuthenticationFailed"), ("Reason", "Authentication failed"), ("Provider", _providerName)]);

            return Outcome<OidcTokenResult>.Failure();
        }
    }

    private static Outcome VerifyState(string? expectedState, string? returnedState)
    {
        if (string.IsNullOrEmpty(expectedState))
        {
            return Outcome.Failure();
        }

        if (string.Equals(expectedState, returnedState, StringComparison.Ordinal))
        {
            return Outcome.Success();
        }

        return Outcome.Failure();
    }

    private async Task<Outcome> ValidateIdTokenAsync(
        string? idToken,
        string accessToken,
        string? expectedNonce,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        if (string.IsNullOrWhiteSpace(idToken))
        {
            Observe(
                LogLevel.Warning,
                values: [("Event", "IdTokenValidationFailed"), ("Reason", "Token response did not contain an id_token"), ("Provider", _providerName)]);
            return Outcome.Failure();
        }

        var handler = new JwtSecurityTokenHandler();
        if (!handler.CanReadToken(idToken))
        {
            Observe(
                LogLevel.Warning,
                values: [("Event", "IdTokenValidationFailed"), ("Reason", "id_token is not a readable JWT"), ("Provider", _providerName)]);
            return Outcome.Failure();
        }

        var signingKeys = await GetSigningKeysAsync(forceRefresh: false, cancellationToken).ConfigureAwait(false);
        if (!signingKeys.IsSuccess || signingKeys.Data == null)
        {
            return Outcome.Failure();
        }

        JwtSecurityToken validatedToken;
        try
        {
            handler.ValidateToken(idToken,
                                  BuildIdTokenValidationParameters(signingKeys.Data),
                                  out var validated);
            validatedToken = (JwtSecurityToken)validated;
        }
        catch (SecurityTokenSignatureKeyNotFoundException)
        {
            var refreshedKeys = await GetSigningKeysAsync(forceRefresh: true, cancellationToken).ConfigureAwait(false);
            if (!refreshedKeys.IsSuccess || refreshedKeys.Data == null)
            {
                return Outcome.Failure();
            }

            try
            {
                handler.ValidateToken(idToken,
                                      BuildIdTokenValidationParameters(refreshedKeys.Data),
                                      out var validated);
                validatedToken = (JwtSecurityToken)validated;
            }
            catch (SecurityTokenException ex)
            {
                Observe(
                    LogLevel.Warning,
                    ex,
                    values: [("Event", "IdTokenValidationFailed"), ("Reason", "id_token validation failed"), ("Provider", _providerName)]);
                return Outcome.Failure();
            }
        }
        catch (SecurityTokenException ex)
        {
            Observe(
                LogLevel.Warning,
                ex,
                values: [("Event", "IdTokenValidationFailed"), ("Reason", "id_token validation failed"), ("Provider", _providerName)]);
            return Outcome.Failure();
        }

        if (!string.IsNullOrEmpty(expectedNonce))
        {
            var nonce = validatedToken.Claims.FirstOrDefault(c => c.Type == "nonce")?.Value;
            if (!string.Equals(nonce, expectedNonce, StringComparison.Ordinal))
            {
                Observe(
                    LogLevel.Warning,
                    values: [("Event", "IdTokenValidationFailed"), ("Reason", "id_token nonce does not match the issued nonce"), ("Provider", _providerName)]);
                return Outcome.Failure();
            }
        }

        var atHash = validatedToken.Claims.FirstOrDefault(c => c.Type == "at_hash")?.Value;
        if (!string.IsNullOrEmpty(atHash))
        {
            if (!VerifyAccessTokenHash(atHash, accessToken, validatedToken.Header.Alg))
            {
                Observe(
                    LogLevel.Warning,
                    values: [("Event", "IdTokenValidationFailed"), ("Reason", "id_token at_hash does not match the access token"), ("Provider", _providerName)]);
                return Outcome.Failure();
            }
        }

        return Outcome.Success();
    }

    private TokenValidationParameters BuildIdTokenValidationParameters(IReadOnlyCollection<SecurityKey> signingKeys)
    {
        ArgumentNullException.ThrowIfNull(signingKeys);

        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _config.Authority,
            ValidateAudience = true,
            ValidAudience = _config.ClientId,
            ValidateLifetime = true,
            ClockSkew = _config.ClockSkew,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            IssuerSigningKeys = signingKeys,
            ValidAlgorithms = AllowedSigningAlgorithms
        };
    }

    private static bool VerifyAccessTokenHash(
        string atHash,
        string accessToken,
        string algorithm)
    {
        if (string.IsNullOrEmpty(accessToken))
        {
            return false;
        }

        using var hasher = ResolveHashAlgorithm(algorithm);
        if (hasher is null)
        {
            return false;
        }

        var digest = hasher.ComputeHash(Encoding.ASCII.GetBytes(accessToken));
        var halfDigest = digest.Take(digest.Length / 2).ToArray();
        var computed = Base64UrlEncode(halfDigest);

        return string.Equals(computed, atHash, StringComparison.Ordinal);
    }

    private static System.Security.Cryptography.HashAlgorithm? ResolveHashAlgorithm(string algorithm)
    {
        if (algorithm.EndsWith("256", StringComparison.Ordinal))
        {
            return System.Security.Cryptography.SHA256.Create();
        }

        if (algorithm.EndsWith("384", StringComparison.Ordinal))
        {
            return System.Security.Cryptography.SHA384.Create();
        }

        if (algorithm.EndsWith("512", StringComparison.Ordinal))
        {
            return System.Security.Cryptography.SHA512.Create();
        }

        return null;
    }

    private static string Base64UrlEncode(byte[] input)
    {
        var base64 = Convert.ToBase64String(input);
        return base64
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public async Task<Outcome<OidcTokenResult>> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);


        try
        {
            var tokenEndpoint = await GetTokenEndpointAsync(cancellationToken).ConfigureAwait(false);
            if (!tokenEndpoint.IsSuccess || tokenEndpoint.Data == null)
            {
                return Outcome<OidcTokenResult>.Failure();
            }

            var tokenRequest = new Dictionary<string, string>
            {
                [OidcWireNames.GRANT_TYPE] = OidcWireNames.REFRESH_TOKEN_GRANT,
                [OidcWireNames.REFRESH_TOKEN] = refreshToken,
                [OidcWireNames.CLIENT_ID] = _config.ClientId,
                [OidcWireNames.CLIENT_SECRET] = _config.ClientSecret
            };

            return await _resilience.ExecuteWithResilienceAsync(
                _resilience.HttpPipeline,
                async ct =>
                {
                    try
                    {
                        var response = await _httpClient.PostAsync(tokenEndpoint.Data,
                                                                   new FormUrlEncodedContent(tokenRequest), ct).ConfigureAwait(false);

                        if (!response.IsSuccessStatusCode)
                        {
                            Observe(
                                LogLevel.Warning,
                                values: [("Event", "TokenRefreshFailed"), ("Reason", "Token endpoint returned non-success status"), ("StatusCode", response.StatusCode), ("Provider", _providerName)]);
                            return Outcome<OidcTokenResult>.Failure();
                        }

                        var tokenResponse = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(_jsonOptions, ct).ConfigureAwait(false);
                        if (tokenResponse == null)
                        {
                            Observe(
                                LogLevel.Warning,
                                values: [("Event", "TokenRefreshFailed"), ("Reason", "Failed to deserialize refresh token response"), ("Provider", _providerName)]);
                            return Outcome<OidcTokenResult>.Failure();
                        }

                        var tokenResult = MapTokenResponse(tokenResponse);
                        return Outcome<OidcTokenResult>.Success(tokenResult);
                    }
                    catch (HttpRequestException ex)
                    {
                        Observe(
                            LogLevel.Warning,
                            ex,
                            values: [("Event", "TokenRefreshFailed"), ("Reason", "Token refresh connection failed"), ("Provider", _providerName)]);
                        return Outcome<OidcTokenResult>.Failure();
                    }
                },
                "Oidc.RefreshTokenAsync",
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Observe(
                LogLevel.Error,
                ex,
                values: [("Event", "TokenRefreshFailed"), ("Reason", "Token refresh failed"), ("Provider", _providerName)]);

            return Outcome<OidcTokenResult>.Failure();
        }
    }

    public virtual async Task<Outcome<OidcUserInfo>> GetUserInfoAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);


        try
        {
            var userInfoEndpoint = await GetUserInfoEndpointAsync(cancellationToken).ConfigureAwait(false);
            if (!userInfoEndpoint.IsSuccess || userInfoEndpoint.Data == null)
            {
                return Outcome<OidcUserInfo>.Failure();
            }

            return await _resilience.ExecuteWithResilienceAsync(
                _resilience.HttpPipeline,
                async ct =>
                {
                    try
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Get, userInfoEndpoint.Data);
                        request.Headers.Authorization = new AuthenticationHeaderValue(OidcWireNames.BEARER, accessToken);

                        var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode)
                        {
                            Observe(
                                LogLevel.Warning,
                                values: [("Event", "UserInfoRequestFailed"), ("Reason", "User info endpoint returned non-success status"), ("StatusCode", response.StatusCode), ("Provider", _providerName)]);
                            return Outcome<OidcUserInfo>.Failure();
                        }

                        var userInfoResponse = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(_jsonOptions, ct).ConfigureAwait(false);
                        if (userInfoResponse == null)
                        {
                            Observe(
                                LogLevel.Warning,
                                values: [("Event", "UserInfoRequestFailed"), ("Reason", "Failed to deserialize user info response"), ("Provider", _providerName)]);
                            return Outcome<OidcUserInfo>.Failure();
                        }

                        var userInfo = _claimsMapper.MapToUserInfo(userInfoResponse, _providerName);
                        return Outcome<OidcUserInfo>.Success(userInfo);
                    }
                    catch (HttpRequestException ex)
                    {
                        Observe(
                            LogLevel.Warning,
                            ex,
                            values: [("Event", "UserInfoRequestFailed"), ("Reason", "User info connection failed"), ("Provider", _providerName)]);
                        return Outcome<OidcUserInfo>.Failure();
                    }
                },
                "Oidc.GetUserInfoAsync",
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Observe(
                LogLevel.Error,
                ex,
                values: [("Event", "UserInfoRequestFailed"), ("Reason", "Get user info failed"), ("Provider", _providerName)]);

            return Outcome<OidcUserInfo>.Failure();
        }
    }

    public async Task<Outcome> ValidateTokenAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            Observe(
                LogLevel.Warning,
                values: [("Event", "TokenValidationFailed"), ("Reason", "Token is missing or empty"), ("Provider", _providerName)]);
            return Outcome.Failure();
        }


        try
        {
            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(token))
            {
                Observe(
                    LogLevel.Warning,
                    values: [("Event", "TokenValidationFailed"), ("Reason", "Token is not a readable JWT"), ("Provider", _providerName)]);
                return Outcome.Failure();
            }

            var signingKeys = await GetSigningKeysAsync(forceRefresh: false, cancellationToken).ConfigureAwait(false);
            if (!signingKeys.IsSuccess || signingKeys.Data == null)
            {
                return Outcome.Failure();
            }

            try
            {
                handler.ValidateToken(token,
                                      BuildValidationParameters(signingKeys.Data),
                                      out _);
                return Outcome.Success();
            }
            catch (SecurityTokenSignatureKeyNotFoundException)
            {
                var refreshedKeys = await GetSigningKeysAsync(forceRefresh: true, cancellationToken).ConfigureAwait(false);
                if (!refreshedKeys.IsSuccess || refreshedKeys.Data == null)
                {
                    return Outcome.Failure();
                }

                handler.ValidateToken(token,
                                      BuildValidationParameters(refreshedKeys.Data),
                                      out _);
                return Outcome.Success();
            }
        }
        catch (SecurityTokenException ex)
        {
            Observe(
                LogLevel.Warning,
                ex,
                values: [("Event", "TokenValidationFailed"), ("Reason", "Token validation failed"), ("Provider", _providerName)]);

            return Outcome.Failure();
        }
        catch (Exception ex)
        {
            Observe(
                LogLevel.Error,
                ex,
                values: [("Event", "TokenValidationFailed"), ("Reason", "Token validation error"), ("Provider", _providerName)]);

            return Outcome.Failure();
        }
    }

    private TokenValidationParameters BuildValidationParameters(IReadOnlyCollection<SecurityKey> signingKeys)
    {
        ArgumentNullException.ThrowIfNull(signingKeys);

        return new TokenValidationParameters
        {
            ValidateIssuer = _config.ValidateIssuer,
            ValidIssuer = _config.Authority,
            ValidateAudience = _config.ValidateAudience,
            ValidAudience = _config.ClientId,
            ValidateLifetime = _config.ValidateLifetime,
            ClockSkew = _config.ClockSkew,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            IssuerSigningKeys = signingKeys,
            ValidAlgorithms = AllowedSigningAlgorithms
        };
    }

    private async Task<Outcome<IReadOnlyCollection<SecurityKey>>> GetSigningKeysAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var authority = _config.Authority ?? string.Empty;

        Lazy<Task<SigningKeysCacheEntry>> lazy;

        if (forceRefresh
            && _signingKeysCache.TryGetValue(authority, out var stale))
        {
            var fresh = new Lazy<Task<SigningKeysCacheEntry>>(() => FetchSigningKeysAsync(CancellationToken.None));
            lazy = _signingKeysCache.TryUpdate(authority, fresh, stale)
                ? fresh
                : _signingKeysCache.GetOrAdd(authority, fresh);
        }
        else
        {
            lazy = _signingKeysCache.GetOrAdd(authority,
                                              key => new Lazy<Task<SigningKeysCacheEntry>>(() => FetchSigningKeysAsync(CancellationToken.None)));
        }

        var entry = await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);

        var isExpired = entry.IsCacheable
            && DateTime.UtcNow - entry.FetchedAtUtc >= _discoveryCacheDuration;

        if (!entry.IsCacheable
            || isExpired)
        {
            var fresh = new Lazy<Task<SigningKeysCacheEntry>>(() => FetchSigningKeysAsync(CancellationToken.None));
            var refreshed = _signingKeysCache.TryUpdate(authority, fresh, lazy)
                ? fresh
                : _signingKeysCache.GetOrAdd(authority, fresh);

            entry = await refreshed.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        return entry.Result;
    }

    private async Task<SigningKeysCacheEntry> FetchSigningKeysAsync(CancellationToken cancellationToken)
    {
        var result = await RetrieveSigningKeysAsync(cancellationToken).ConfigureAwait(false);
        return new SigningKeysCacheEntry
        {
            Result = result,
            FetchedAtUtc = DateTime.UtcNow,
            IsCacheable = result.IsSuccess && result.Data != null
        };
    }

    private async Task<Outcome<IReadOnlyCollection<SecurityKey>>> RetrieveSigningKeysAsync(CancellationToken cancellationToken)
    {
        var discovery = await GetDiscoveryDocumentAsync(cancellationToken).ConfigureAwait(false);
        if (!discovery.IsSuccess || discovery.Data == null)
        {
            return Outcome<IReadOnlyCollection<SecurityKey>>.Failure();
        }

        var jwksUri = ExtractString(discovery.Data.TryGetValue(OidcWireNames.JWKS_URI, out var jwksUriValue) ? jwksUriValue : null);
        if (string.IsNullOrWhiteSpace(jwksUri))
        {
            Observe(
                LogLevel.Warning,
                values: [("Event", "SigningKeysRetrievalFailed"), ("Reason", "JWKS endpoint not found in discovery document"), ("Provider", _providerName)]);
            return Outcome<IReadOnlyCollection<SecurityKey>>.Failure();
        }

        var httpsCheck = EnsureHttpsAllowed(jwksUri);
        if (!httpsCheck.IsSuccess)
        {
            return Outcome<IReadOnlyCollection<SecurityKey>>.Failure();
        }

        return await _resilience.ExecuteWithResilienceAsync(
            _resilience.HttpPipeline,
            async ct =>
            {
                try
                {
                    var response = await _httpClient.GetAsync(jwksUri, ct).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        Observe(
                            LogLevel.Warning,
                            values: [("Event", "SigningKeysRetrievalFailed"), ("Reason", "JWKS request returned non-success status"), ("StatusCode", response.StatusCode), ("Provider", _providerName)]);
                        return Outcome<IReadOnlyCollection<SecurityKey>>.Failure();
                    }

                    var jwksJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var keySet = new JsonWebKeySet(jwksJson);
                    var keys = keySet.GetSigningKeys();
                    if (keys.Count == 0)
                    {
                        Observe(
                            LogLevel.Warning,
                            values: [("Event", "SigningKeysRetrievalFailed"), ("Reason", "JWKS contained no signing keys"), ("Provider", _providerName)]);
                        return Outcome<IReadOnlyCollection<SecurityKey>>.Failure();
                    }

                    return Outcome<IReadOnlyCollection<SecurityKey>>.Success(keys.ToList());
                }
                catch (HttpRequestException ex)
                {
                    Observe(
                        LogLevel.Warning,
                        ex,
                        values: [("Event", "SigningKeysRetrievalFailed"), ("Reason", "JWKS request connection failed"), ("Provider", _providerName)]);
                    return Outcome<IReadOnlyCollection<SecurityKey>>.Failure();
                }
            },
            "Oidc.RetrieveSigningKeys",
            cancellationToken).ConfigureAwait(false);
    }

    private static string? ExtractString(object? value)
    {
        if (value is string text)
        {
            return text;
        }

        if (value is JsonElement element
            && element.ValueKind == JsonValueKind.String)
        {
            return element.GetString();
        }

        return null;
    }

    private Outcome EnsureHttpsAllowed(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        if (!_config.RequireHttps
            && _config.AllowInsecureValidation)
        {
            return Outcome.Success();
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps)
        {
            return Outcome.Success();
        }

        return Outcome.Failure();
    }

    public virtual async Task<Outcome<string>> GetAuthorizationUrlAsync(
        string? state = null,
        string? nonce = null,
        string? codeChallenge = null,
        string? codeChallengeMethod = null,
        CancellationToken cancellationToken = default)
    {

        try
        {
            var authEndpoint = await GetAuthorizationEndpointAsync(cancellationToken).ConfigureAwait(false);
            if (!authEndpoint.IsSuccess || authEndpoint.Data == null)
            {
                return Outcome<string>.Failure();
            }

            var parameters = new Dictionary<string, string>
            {
                ["response_type"] = string.Join(" ", _config.ResponseTypes),
                [OidcWireNames.CLIENT_ID] = _config.ClientId,
                [OidcWireNames.REDIRECT_URI] = _config.RedirectUri ?? string.Empty,
                [OidcWireNames.SCOPE] = string.Join(" ", _config.Scopes)
            };

            if (!string.IsNullOrEmpty(state))
            {
                parameters["state"] = state;
            }
            if (!string.IsNullOrEmpty(nonce))
            {
                parameters["nonce"] = nonce;
            }
            if (!string.IsNullOrEmpty(codeChallenge))
            {
                parameters["code_challenge"] = codeChallenge;
            }
            if (!string.IsNullOrEmpty(codeChallengeMethod))
            {
                parameters["code_challenge_method"] = codeChallengeMethod;
            }
            if (!string.IsNullOrEmpty(_config.ResponseMode))
            {
                parameters["response_mode"] = _config.ResponseMode;
            }

            foreach (var param in _config.AdditionalParameters)
            {
                parameters[param.Key] = param.Value;
            }

            var queryString = string.Join("&", parameters.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
            var authUrl = $"{authEndpoint.Data}?{queryString}";

            return Outcome<string>.Success(authUrl);
        }
        catch (Exception ex)
        {
            Observe(
                LogLevel.Error,
                ex,
                values: [("Event", "AuthorizationUrlFailed"), ("Reason", "Get authorization URL failed"), ("Provider", _providerName)]);

            return Outcome<string>.Failure();
        }
    }

    public async Task<Outcome> RevokeTokenAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);


        try
        {
            var revokeEndpoint = await GetRevocationEndpointAsync(cancellationToken).ConfigureAwait(false);
            if (!revokeEndpoint.IsSuccess || revokeEndpoint.Data == null)
            {
                return Outcome.Failure();
            }

            var revokeRequest = new Dictionary<string, string>
            {
                ["token"] = token,
                [OidcWireNames.CLIENT_ID] = _config.ClientId,
                [OidcWireNames.CLIENT_SECRET] = _config.ClientSecret
            };

            return await _resilience.ExecuteWithResilienceAsync(
                _resilience.HttpPipeline,
                async ct =>
                {
                    try
                    {
                        var response = await _httpClient.PostAsync(revokeEndpoint.Data,
                                                                   new FormUrlEncodedContent(revokeRequest), ct).ConfigureAwait(false);

                        if (!response.IsSuccessStatusCode)
                        {
                            Observe(
                                LogLevel.Warning,
                                values: [("Event", "TokenRevocationFailed"), ("Reason", "Revocation endpoint returned non-success status"), ("StatusCode", response.StatusCode), ("Provider", _providerName)]);
                            return Outcome.Failure();
                        }

                        return Outcome.Success();
                    }
                    catch (HttpRequestException ex)
                    {
                        Observe(
                            LogLevel.Warning,
                            ex,
                            values: [("Event", "TokenRevocationFailed"), ("Reason", "Token revocation connection failed"), ("Provider", _providerName)]);
                        return Outcome.Failure();
                    }
                },
                "Oidc.RevokeTokenAsync",
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Observe(
                LogLevel.Error,
                ex,
                values: [("Event", "TokenRevocationFailed"), ("Reason", "Token revocation failed"), ("Provider", _providerName)]);

            return Outcome.Failure();
        }
    }

    public async Task<Outcome> LogoutAsync(
        string? idToken = null,
        CancellationToken cancellationToken = default)
    {

        try
        {
            var logoutEndpoint = await GetEndSessionEndpointAsync(cancellationToken).ConfigureAwait(false);
            if (!logoutEndpoint.IsSuccess || logoutEndpoint.Data == null)
            {
                return Outcome.Failure();
            }

            var logoutUrl = logoutEndpoint.Data;
            if (!string.IsNullOrEmpty(idToken))
            {
                logoutUrl += $"?id_token_hint={Uri.EscapeDataString(idToken)}";
                if (!string.IsNullOrEmpty(_config.PostLogoutRedirectUri))
                {
                    logoutUrl += $"&post_logout_redirect_uri={Uri.EscapeDataString(_config.PostLogoutRedirectUri)}";
                }
            }

            return Outcome.Success();
        }
        catch (Exception ex)
        {
            Observe(
                LogLevel.Error,
                ex,
                values: [("Event", "LogoutFailed"), ("Reason", "Logout failed"), ("Provider", _providerName)]);

            return Outcome.Failure();
        }
    }

    [Operation("ValidateConfigurationAsync")]
    public async Task<Outcome> ValidateConfigurationAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome.Failure();
        }


        try
        {
            if (!IsConfigured)
            {
                Observe(
                    LogLevel.Warning,
                    values: [("Event", "ConfigurationValidationFailed"), ("Reason", "OIDC provider configuration is incomplete"), ("Provider", _providerName)]);
                return Outcome.Failure();
            }

            if (!_config.AllowInsecureValidation)
            {
                var disabled = new List<string>();
                if (!_config.ValidateIssuer)
                {
                    disabled.Add(nameof(_config.ValidateIssuer));
                }
                if (!_config.ValidateAudience)
                {
                    disabled.Add(nameof(_config.ValidateAudience));
                }
                if (!_config.ValidateLifetime)
                {
                    disabled.Add(nameof(_config.ValidateLifetime));
                }
                if (!_config.ValidateIssuerSigningKey)
                {
                    disabled.Add(nameof(_config.ValidateIssuerSigningKey));
                }

                if (disabled.Count > 0)
                {
                    Observe(
                        LogLevel.Error,
                        values: [("Event", "ConfigurationValidationFailed"), ("Reason", "OIDC provider has insecure validation toggles disabled; these must remain enabled in production"), ("DisabledToggles", string.Join(", ", disabled)), ("Provider", _providerName)]);
                    return Outcome.Failure();
                }
            }

            var authorityHttps = EnsureHttpsAllowed(_config.Authority);
            if (!authorityHttps.IsSuccess)
            {
                Observe(
                    LogLevel.Error,
                    values: [("Event", "ConfigurationValidationFailed"), ("Reason", "Authority must use HTTPS when RequireHttps is enabled"), ("Provider", _providerName)]);
                return Outcome.Failure();
            }

            if (!string.IsNullOrEmpty(_config.RedirectUri))
            {
                var redirectHttps = EnsureHttpsAllowed(_config.RedirectUri);
                if (!redirectHttps.IsSuccess)
                {
                    Observe(
                        LogLevel.Error,
                        values: [("Event", "ConfigurationValidationFailed"), ("Reason", "RedirectUri must use HTTPS when RequireHttps is enabled"), ("Provider", _providerName)]);
                    return Outcome.Failure();
                }
            }

            var discoveryResult = await GetDiscoveryDocumentAsync(cancellationToken).ConfigureAwait(false);
            if (!discoveryResult.IsSuccess || discoveryResult.Data == null)
            {
                Observe(
                    LogLevel.Error,
                    values: [("Event", "ConfigurationValidationFailed"), ("Reason", "Failed to discover OIDC endpoints"), ("Provider", _providerName)]);
                return Outcome.Failure();
            }

            return Outcome.Success();
        }
        catch (Exception ex)
        {
            Observe(
                LogLevel.Error,
                ex,
                values: [("Event", "ConfigurationValidationFailed"), ("Reason", "Configuration validation failed"), ("Provider", _providerName)]);
            return Outcome.Failure();
        }
    }

    private async Task<Outcome<Dictionary<string, object>>> GetDiscoveryDocumentAsync(CancellationToken cancellationToken)
    {
        var authority = _config.Authority ?? string.Empty;
        var lazy = _discoveryCache.GetOrAdd(authority,
                                            key => new Lazy<Task<DiscoveryCacheEntry>>(() => FetchDiscoveryDocumentAsync(key, CancellationToken.None)));

        var entry = await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);

        var isExpired = entry.IsCacheable
            && DateTime.UtcNow - entry.FetchedAtUtc >= _discoveryCacheDuration;

        if (!entry.IsCacheable
            || isExpired)
        {
            var fresh = new Lazy<Task<DiscoveryCacheEntry>>(() => FetchDiscoveryDocumentAsync(authority, CancellationToken.None));
            var refreshed = _discoveryCache.TryUpdate(authority, fresh, lazy)
                ? fresh
                : _discoveryCache.GetOrAdd(authority, fresh);

            entry = await refreshed.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        return entry.Result;
    }

    private async Task<DiscoveryCacheEntry> FetchDiscoveryDocumentAsync(
        string authority,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authority);

        var result = await RetrieveDiscoveryDocumentAsync(authority, cancellationToken).ConfigureAwait(false);
        return new DiscoveryCacheEntry
        {
            Result = result,
            FetchedAtUtc = DateTime.UtcNow,
            IsCacheable = result.IsSuccess && result.Data != null
        };
    }

    private async Task<Outcome<Dictionary<string, object>>> RetrieveDiscoveryDocumentAsync(
        string authority,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authority);

        var discoveryUrl = $"{authority.TrimEnd('/')}{OidcWireNames.DISCOVERY_PATH}";

        var httpsCheck = EnsureHttpsAllowed(discoveryUrl);
        if (!httpsCheck.IsSuccess)
        {
            Observe(
                LogLevel.Warning,
                values: [("Event", "DiscoveryRequestFailed"), ("Reason", "Discovery endpoint is not HTTPS but HTTPS is required"), ("Provider", _providerName)]);
            return Outcome<Dictionary<string, object>>.Failure();
        }

        return await _resilience.ExecuteWithResilienceAsync(
            _resilience.HttpPipeline,
            async ct =>
            {
                try
                {
                    var response = await _httpClient.GetAsync(discoveryUrl, ct).ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        Observe(
                            LogLevel.Warning,
                            values: [("Event", "DiscoveryRequestFailed"), ("Reason", "Discovery document request returned non-success status"), ("StatusCode", response.StatusCode), ("Provider", _providerName)]);
                        return Outcome<Dictionary<string, object>>.Failure();
                    }

                    var discoveryDoc = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(_jsonOptions, ct).ConfigureAwait(false);
                    if (discoveryDoc == null)
                    {
                        Observe(
                            LogLevel.Warning,
                            values: [("Event", "DiscoveryRequestFailed"), ("Reason", "Failed to deserialize discovery document"), ("Provider", _providerName)]);
                        return Outcome<Dictionary<string, object>>.Failure();
                    }

                    return Outcome<Dictionary<string, object>>.Success(discoveryDoc);
                }
                catch (HttpRequestException ex)
                {
                    Observe(
                        LogLevel.Warning,
                        ex,
                        values: [("Event", "DiscoveryRequestFailed"), ("Reason", "Discovery document connection failed"), ("Provider", _providerName)]);
                    return Outcome<Dictionary<string, object>>.Failure();
                }
            },
            "Oidc.RetrieveDiscoveryDocument",
            cancellationToken).ConfigureAwait(false);
    }

    protected async Task<Outcome<string>> GetAuthorizationEndpointAsync(CancellationToken cancellationToken)
    {
        return await GetEndpointAsync(
            OidcWireNames.AUTHORIZATION_ENDPOINT,
            "Authorization endpoint not found in discovery document",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<Outcome<string>> GetTokenEndpointAsync(CancellationToken cancellationToken)
    {
        return await GetEndpointAsync(
            OidcWireNames.TOKEN_ENDPOINT,
            "Token endpoint not found in discovery document",
            cancellationToken).ConfigureAwait(false);
    }

    protected async Task<Outcome<string>> GetUserInfoEndpointAsync(CancellationToken cancellationToken)
    {
        return await GetEndpointAsync(
            OidcWireNames.USERINFO_ENDPOINT,
            "UserInfo endpoint not found in discovery document",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<Outcome<string>> GetRevocationEndpointAsync(CancellationToken cancellationToken)
    {
        return await GetEndpointAsync(
            OidcWireNames.REVOCATION_ENDPOINT,
            "Revocation endpoint not found in discovery document",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<Outcome<string>> GetEndSessionEndpointAsync(CancellationToken cancellationToken)
    {
        return await GetEndpointAsync(
            OidcWireNames.END_SESSION_ENDPOINT,
            "End session endpoint not found in discovery document",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<Outcome<string>> GetEndpointAsync(
        string discoveryKey,
        string notFoundReason,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(discoveryKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(notFoundReason);

        var discovery = await GetDiscoveryDocumentAsync(cancellationToken).ConfigureAwait(false);
        if (!discovery.IsSuccess || discovery.Data == null)
        {
            return Outcome<string>.Failure();
        }

        var endpoint = ExtractString(discovery.Data.TryGetValue(discoveryKey, out var value) ? value : null);
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            return Outcome<string>.Success(endpoint);
        }

        Observe(
            LogLevel.Warning,
            values: [("Event", "EndpointNotFound"), ("Reason", notFoundReason), ("Provider", _providerName)]);
        return Outcome<string>.Failure();
    }

    private OidcTokenResult MapTokenResponse(Dictionary<string, object> tokenResponse)
    {
        ArgumentNullException.ThrowIfNull(tokenResponse);

        var accessToken = tokenResponse.GetValueOrDefault(OidcWireNames.ACCESS_TOKEN)?.ToString() ?? string.Empty;
        var refreshToken = tokenResponse.GetValueOrDefault(OidcWireNames.REFRESH_TOKEN)?.ToString();
        var idToken = tokenResponse.GetValueOrDefault(OidcWireNames.ID_TOKEN)?.ToString();
        var tokenType = tokenResponse.GetValueOrDefault(OidcWireNames.TOKEN_TYPE)?.ToString() ?? OidcWireNames.BEARER;
        var expiresIn = tokenResponse.GetValueOrDefault(OidcWireNames.EXPIRES_IN);
        var scope = tokenResponse.GetValueOrDefault(OidcWireNames.SCOPE)?.ToString();

        var expiresAt = DateTime.UtcNow.Add(_config.TokenLifetime);
        if (expiresIn != null && int.TryParse(expiresIn.ToString(), out var expiresInSeconds))
        {
            expiresAt = DateTime.UtcNow.AddSeconds(expiresInSeconds);
        }

        var refreshExpiresAt = DateTime.UtcNow.Add(_config.RefreshTokenLifetime);
        if (tokenResponse.TryGetValue(OidcWireNames.REFRESH_EXPIRES_IN, out var refreshExpiresIn) &&
            int.TryParse(refreshExpiresIn.ToString(), out var refreshExpiresInSeconds))
        {
            refreshExpiresAt = DateTime.UtcNow.AddSeconds(refreshExpiresInSeconds);
        }

        var scopes = new List<string>();
        if (!string.IsNullOrEmpty(scope))
        {
            scopes.AddRange(scope.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        var additionalProperties = new Dictionary<string, object>();
        foreach (var pair in tokenResponse)
        {
            if (SecretTokenResponseKeys.Contains(pair.Key))
            {
                continue;
            }

            additionalProperties[pair.Key] = pair.Value;
        }

        return new OidcTokenResult
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            IdToken = idToken,
            ExpiresAt = expiresAt,
            RefreshExpiresAt = refreshExpiresAt,
            TokenType = tokenType,
            Scopes = scopes,
            AdditionalProperties = additionalProperties
        };
    }

    private static readonly HashSet<string> SecretTokenResponseKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        OidcWireNames.ACCESS_TOKEN,
        OidcWireNames.REFRESH_TOKEN,
        OidcWireNames.ID_TOKEN
    };
}
