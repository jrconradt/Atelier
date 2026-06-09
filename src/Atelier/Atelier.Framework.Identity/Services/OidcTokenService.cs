using Atelier.Framework.Primitives;
using Atelier.Framework.Identity.Interfaces;
using Atelier.Framework.Identity.Models;
using Atelier.Framework.Attributes;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Atelier.Framework.Identity.Services;

[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
[Infrastructure(InfrastructureLifetime.Singleton)]
public partial class OidcTokenService : IOidcTokenService, IAtelier
{
    [Requisite] protected readonly IOidcProviderFactory _providerFactory = null!;
    [Requisite] protected readonly IOidcClaimsMapper _claimsMapper = null!;

    public async Task<Outcome<ClaimsPrincipal>> ValidateTokenAsync(
        string token,
        string? providerName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "OidcProvider", providerName ?? "default");

        try
        {
            var provider = await GetProviderAsync(providerName, cancellationToken).ConfigureAwait(false);
            if (!provider.IsSuccess || provider.Data == null)
            {
                return Outcome<ClaimsPrincipal>.Failure();
            }

            var validationResult = await provider.Data.ValidateTokenAsync(token, cancellationToken).ConfigureAwait(false);
            if (!validationResult.IsSuccess)
            {
                return Outcome<ClaimsPrincipal>.Failure();
            }

            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(token))
            {
                Observe(
                    LogLevel.Warning,
                    values: [("Event", "TokenValidationFailed"), ("Reason", "Token is not a readable JWT")]);
                return Outcome<ClaimsPrincipal>.Failure();
            }

            var jwt = handler.ReadJwtToken(token);
            var validatedClaims = new Dictionary<string, object>();
            foreach (var claim in jwt.Claims)
            {
                validatedClaims[claim.Type] = claim.Value;
            }

            var userInfo = _claimsMapper.MapToUserInfo(validatedClaims, provider.Data.ProviderName);
            var claimsPrincipal = _claimsMapper.MapToClaimsPrincipal(userInfo, provider.Data.ProviderName);
            return Outcome<ClaimsPrincipal>.Success(claimsPrincipal);
        }
        catch (Exception ex)
        {
            Observe(
                LogLevel.Error,
                ex,
                values: [("Event", "TokenValidationFailed"), ("Reason", "Token validation failed")]);

            return Outcome<ClaimsPrincipal>.Failure();
        }
    }

    public async Task<Outcome<OidcUserInfo>> ExtractUserInfoAsync(
        string token,
        string? providerName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "OidcProvider", providerName ?? "default");

        try
        {
            var provider = await GetProviderAsync(providerName, cancellationToken).ConfigureAwait(false);
            if (!provider.IsSuccess || provider.Data == null)
            {
                return Outcome<OidcUserInfo>.Failure();
            }

            var userInfoResult = await provider.Data.GetUserInfoAsync(token, cancellationToken).ConfigureAwait(false);
            if (!userInfoResult.IsSuccess || userInfoResult.Data == null)
            {
                return Outcome<OidcUserInfo>.Failure();
            }

            return Outcome<OidcUserInfo>.Success(userInfoResult.Data);
        }
        catch (Exception ex)
        {
            Observe(
                LogLevel.Error,
                ex,
                values: [("Event", "UserInfoExtractionFailed"), ("Reason", "User info extraction failed")]);

            return Outcome<OidcUserInfo>.Failure();
        }
    }

    public async Task<Outcome<OidcTokenResult>> RefreshTokenAsync(
        string refreshToken,
        string? providerName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "OidcProvider", providerName ?? "default");

        try
        {
            var provider = await GetProviderAsync(providerName, cancellationToken).ConfigureAwait(false);
            if (!provider.IsSuccess || provider.Data == null)
            {
                return Outcome<OidcTokenResult>.Failure();
            }

            var refreshResult = await provider.Data.RefreshTokenAsync(refreshToken, cancellationToken).ConfigureAwait(false);
            if (!refreshResult.IsSuccess || refreshResult.Data == null)
            {
                return Outcome<OidcTokenResult>.Failure();
            }

            return Outcome<OidcTokenResult>.Success(refreshResult.Data);
        }
        catch (Exception ex)
        {
            Observe(
                LogLevel.Error,
                ex,
                values: [("Event", "TokenRefreshFailed"), ("Reason", "Token refresh failed")]);

            return Outcome<OidcTokenResult>.Failure();
        }
    }

    public async Task<Outcome> IsTokenValidAsync(
        string token,
        string? providerName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "OidcProvider", providerName ?? "default");

        try
        {
            var provider = await GetProviderAsync(providerName, cancellationToken).ConfigureAwait(false);
            if (!provider.IsSuccess || provider.Data == null)
            {
                return Outcome.Failure();
            }

            var validationResult = await provider.Data.ValidateTokenAsync(token, cancellationToken).ConfigureAwait(false);
            if (!validationResult.IsSuccess)
            {
                return Outcome.Failure();
            }

            return Outcome.Success();
        }
        catch (Exception ex)
        {
            Observe(
                LogLevel.Error,
                ex,
                values: [("Event", "TokenValidationCheckFailed"), ("Reason", "Token validation check failed")]);

            return Outcome.Failure();
        }
    }

    public async Task<Outcome> RevokeTokenAsync(
        string token,
        string? providerName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "OidcProvider", providerName ?? "default");

        try
        {
            var provider = await GetProviderAsync(providerName, cancellationToken).ConfigureAwait(false);
            if (!provider.IsSuccess || provider.Data == null)
            {
                return Outcome.Failure();
            }

            var revokeResult = await provider.Data.RevokeTokenAsync(token, cancellationToken).ConfigureAwait(false);
            if (!revokeResult.IsSuccess)
            {
                return Outcome.Failure();
            }

            return Outcome.Success();
        }
        catch (Exception ex)
        {
            Observe(
                LogLevel.Error,
                ex,
                values: [("Event", "TokenRevocationFailed"), ("Reason", "Token revocation failed")]);

            return Outcome.Failure();
        }
    }

    public async Task<Outcome<Dictionary<string, object>>> ExtractClaimsAsync(
        string token,
        string? providerName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "OidcProvider", providerName ?? "default");

        try
        {
            var provider = await GetProviderAsync(providerName, cancellationToken).ConfigureAwait(false);
            if (!provider.IsSuccess || provider.Data == null)
            {
                return Outcome<Dictionary<string, object>>.Failure();
            }

            var handler = new JwtSecurityTokenHandler();
            if (handler.CanReadToken(token))
            {
                var validationResult = await provider.Data.ValidateTokenAsync(token, cancellationToken).ConfigureAwait(false);
                if (!validationResult.IsSuccess)
                {
                    return Outcome<Dictionary<string, object>>.Failure();
                }

                var jwt = handler.ReadJwtToken(token);
                var jwtClaims = new Dictionary<string, object>();
                foreach (var claim in jwt.Claims)
                {
                    jwtClaims[claim.Type] = claim.Value;
                }

                var jwtUserInfo = _claimsMapper.MapToUserInfo(jwtClaims, provider.Data.ProviderName);
                if (!string.IsNullOrEmpty(jwtUserInfo.Subject))
                {
                    return Outcome<Dictionary<string, object>>.Success(BuildClaims(jwtUserInfo));
                }
            }

            var userInfoResult = await ExtractUserInfoAsync(token,
                                                            providerName,
                                                            cancellationToken).ConfigureAwait(false);
            if (!userInfoResult.IsSuccess || userInfoResult.Data == null)
            {
                return Outcome<Dictionary<string, object>>.Failure();
            }

            return Outcome<Dictionary<string, object>>.Success(BuildClaims(userInfoResult.Data));
        }
        catch (Exception ex)
        {
            Observe(
                LogLevel.Error,
                ex,
                values: [("Event", "ClaimsExtractionFailed"), ("Reason", "Claims extraction failed")]);

            return Outcome<Dictionary<string, object>>.Failure();
        }
    }

    private static Dictionary<string, object> BuildClaims(OidcUserInfo userInfo)
    {
        var claims = new Dictionary<string, object>
        {
            ["sub"] = userInfo.Subject,
            ["name"] = userInfo.Name ?? string.Empty,
            ["email"] = userInfo.Email ?? string.Empty,
            ["preferred_username"] = userInfo.Username ?? string.Empty,
            ["email_verified"] = userInfo.EmailVerified,
            ["phone_number_verified"] = userInfo.PhoneNumberVerified
        };

        if (!string.IsNullOrEmpty(userInfo.Picture))
        {
            claims["picture"] = userInfo.Picture;
        }

        if (!string.IsNullOrEmpty(userInfo.Locale))
        {
            claims["locale"] = userInfo.Locale;
        }

        if (!string.IsNullOrEmpty(userInfo.ZoneInfo))
        {
            claims["zoneinfo"] = userInfo.ZoneInfo;
        }

        if (!string.IsNullOrEmpty(userInfo.PhoneNumber))
        {
            claims["phone_number"] = userInfo.PhoneNumber;
        }

        if (!string.IsNullOrEmpty(userInfo.Address))
        {
            claims["address"] = userInfo.Address;
        }

        if (userInfo.Roles.Any())
        {
            claims["roles"] = userInfo.Roles;
        }

        if (userInfo.Scopes.Any())
        {
            claims["scope"] = string.Join(" ", userInfo.Scopes);
        }

        if (!string.IsNullOrEmpty(userInfo.TenantId))
        {
            claims["tenant_id"] = userInfo.TenantId;
        }

        if (!string.IsNullOrEmpty(userInfo.SessionId))
        {
            claims["sid"] = userInfo.SessionId;
        }

        foreach (var claim in userInfo.AdditionalClaims)
        {
            claims[claim.Key] = claim.Value;
        }

        return claims;
    }

    private async Task<Outcome<IOidcProvider>> GetProviderAsync(
        string? providerName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(providerName))
        {
            return await _providerFactory.GetDefaultProviderAsync(cancellationToken).ConfigureAwait(false);
        }

        return await _providerFactory.GetProviderAsync(providerName, cancellationToken).ConfigureAwait(false);
    }
}
