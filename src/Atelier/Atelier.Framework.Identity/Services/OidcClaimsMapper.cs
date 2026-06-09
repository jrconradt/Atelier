using Atelier.Framework.Primitives;
using Atelier.Framework.Attributes;
using Atelier.Framework.Identity.Configuration;
using Atelier.Framework.Identity.Interfaces;
using Atelier.Framework.Identity.Models;
using Atelier.Framework.Requisitions;
using Atelier.Framework.Observability;

using System.Security.Claims;

namespace Atelier.Framework.Identity.Services;

[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
[Infrastructure(InfrastructureLifetime.Singleton)]
public partial class OidcClaimsMapper : IOidcClaimsMapper
{
    [Runtime] private readonly OidcConfiguration _configuration = null!;

    public OidcUserInfo MapToUserInfo(
        Dictionary<string, object> claims,
        string providerName)
    {
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        var providerConfig = _configuration.GetProvider(providerName);
        var mapping = providerConfig?.ClaimsMapping ?? new ClaimsMappingConfiguration();

        return new OidcUserInfo
        {
            Subject = OidcClaimHelpers.GetClaimValue(claims, mapping.SubjectClaim) ?? string.Empty,
            Name = OidcClaimHelpers.GetClaimValue(claims, mapping.NameClaim),
            Email = OidcClaimHelpers.GetClaimValue(claims, mapping.EmailClaim),
            Username = OidcClaimHelpers.GetClaimValue(claims, mapping.UsernameClaim),
            Picture = OidcClaimHelpers.GetClaimValue(claims, "picture"),
            Locale = OidcClaimHelpers.GetClaimValue(claims, "locale"),
            ZoneInfo = OidcClaimHelpers.GetClaimValue(claims, "zoneinfo"),
            EmailVerified = OidcClaimHelpers.GetBooleanClaimValue(claims, "email_verified"),
            PhoneNumberVerified = OidcClaimHelpers.GetBooleanClaimValue(claims, "phone_number_verified"),
            PhoneNumber = OidcClaimHelpers.GetClaimValue(claims, "phone_number"),
            Address = OidcClaimHelpers.GetClaimValue(claims, "address"),
            Roles = OidcClaimHelpers.GetArrayClaimValues(claims, mapping.RolesClaim),
            Scopes = OidcClaimHelpers.GetArrayClaimValues(claims, mapping.ScopesClaim),
            TenantId = OidcClaimHelpers.GetClaimValue(claims, mapping.TenantIdClaim),
            SessionId = OidcClaimHelpers.GetClaimValue(claims, mapping.SessionIdClaim),
            AdditionalClaims = OidcClaimHelpers.GetAdditionalClaims(
                claims,
                new[]
                {
                    mapping.SubjectClaim,
                    mapping.NameClaim,
                    mapping.EmailClaim,
                    mapping.UsernameClaim,
                    mapping.RolesClaim,
                    mapping.ScopesClaim,
                    mapping.TenantIdClaim,
                    mapping.SessionIdClaim,
                    "picture",
                    "locale",
                    "zoneinfo",
                    "email_verified",
                    "phone_number_verified",
                    "phone_number",
                    "address"
                })
        };
    }

    public ClaimsPrincipal MapToClaimsPrincipal(
        OidcUserInfo userInfo,
        string providerName)
    {
        ArgumentNullException.ThrowIfNull(userInfo);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userInfo.Subject),
            new(ClaimTypes.Name, userInfo.Name ?? string.Empty),
            new(ClaimTypes.Email, userInfo.Email ?? string.Empty)
        };

        if (!string.IsNullOrEmpty(userInfo.Username))
        {
            claims.Add(new(ClaimTypes.GivenName, userInfo.Username));
        }

        if (!string.IsNullOrEmpty(userInfo.Picture))
        {
            claims.Add(new("picture", userInfo.Picture));
        }

        if (!string.IsNullOrEmpty(userInfo.Locale))
        {
            claims.Add(new("locale", userInfo.Locale));
        }

        if (!string.IsNullOrEmpty(userInfo.ZoneInfo))
        {
            claims.Add(new("zoneinfo", userInfo.ZoneInfo));
        }

        claims.Add(new("email_verified", userInfo.EmailVerified.ToString().ToLower()));
        claims.Add(new("phone_number_verified", userInfo.PhoneNumberVerified.ToString().ToLower()));

        if (!string.IsNullOrEmpty(userInfo.PhoneNumber))
        {
            claims.Add(new(ClaimTypes.MobilePhone, userInfo.PhoneNumber));
        }

        if (!string.IsNullOrEmpty(userInfo.Address))
        {
            claims.Add(new(ClaimTypes.StreetAddress, userInfo.Address));
        }

        foreach (var role in userInfo.Roles)
        {
            claims.Add(new(ClaimTypes.Role, role));
        }

        foreach (var scope in userInfo.Scopes)
        {
            claims.Add(new("scope", scope));
        }

        if (!string.IsNullOrEmpty(userInfo.TenantId))
        {
            claims.Add(new("tenant_id", userInfo.TenantId));
        }

        if (!string.IsNullOrEmpty(userInfo.SessionId))
        {
            claims.Add(new("sid", userInfo.SessionId));
        }

        foreach (var claim in userInfo.AdditionalClaims)
        {
            claims.Add(new(claim.Key, claim.Value.ToString() ?? string.Empty));
        }

        var identity = new ClaimsIdentity(claims, "oidc");
        return new ClaimsPrincipal(identity);
    }

    public Dictionary<string, object> MapFromClaims(
        ClaimsPrincipal principal,
        string providerName)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        var claims = new Dictionary<string, object>();
        var providerConfig = _configuration.GetProvider(providerName);
        var mapping = providerConfig?.ClaimsMapping ?? new ClaimsMappingConfiguration();

        foreach (var claim in principal.Claims)
        {
            var mappedName = MapClaimName(claim.Type, providerName);
            var mappedValue = MapClaimValue(claim.Value, claim.Type, providerName);
            claims[mappedName] = mappedValue;
        }

        return claims;
    }

    public string MapClaimName(
        string originalClaimName,
        string providerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalClaimName);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        var providerConfig = _configuration.GetProvider(providerName);
        var mapping = providerConfig?.ClaimsMapping ?? new ClaimsMappingConfiguration();

        return originalClaimName switch
        {
            ClaimTypes.NameIdentifier => mapping.SubjectClaim,
            ClaimTypes.Name => mapping.NameClaim,
            ClaimTypes.Email => mapping.EmailClaim,
            ClaimTypes.GivenName => mapping.UsernameClaim,
            ClaimTypes.Role => mapping.RolesClaim,
            "scope" => mapping.ScopesClaim,
            "tenant_id" => mapping.TenantIdClaim,
            "sid" => mapping.SessionIdClaim,
            _ => mapping.CustomMappings.TryGetValue(originalClaimName, out var customMapping)
                ? customMapping
                : originalClaimName
        };
    }

    public object MapClaimValue(
        object originalValue,
        string claimName,
        string providerName)
    {
        ArgumentNullException.ThrowIfNull(originalValue);
        ArgumentException.ThrowIfNullOrWhiteSpace(claimName);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        return originalValue;
    }

}
