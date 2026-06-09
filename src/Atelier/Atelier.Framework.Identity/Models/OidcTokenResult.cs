using Atelier.Framework.Attributes;
using System.Security.Claims;
using System.Text.Json.Serialization;

namespace Atelier.Framework.Identity.Models;

[ContractAttribute("OidcTokenResult", Version = "1.0")]
public class OidcTokenResult
{
    [JsonIgnore]
    public required string AccessToken { get; set; }

    [JsonIgnore]
    public string? RefreshToken { get; set; }

    [JsonIgnore]
    public string? IdToken { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RefreshExpiresAt { get; set; }
    public string TokenType { get; set; } = "Bearer";
    public List<string> Scopes { get; set; } = new();
    public Dictionary<string, object> AdditionalProperties { get; set; } = new();

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool CanRefresh => !string.IsNullOrEmpty(RefreshToken) &&
                               (RefreshExpiresAt == null || DateTime.UtcNow < RefreshExpiresAt);
}

[ContractAttribute("OidcUserInfo", Version = "1.0")]
public class OidcUserInfo
{
    public required string Subject { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Username { get; set; }
    public string? Picture { get; set; }
    public string? Locale { get; set; }
    public string? ZoneInfo { get; set; }
    public bool EmailVerified { get; set; }
    public bool PhoneNumberVerified { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Address { get; set; }
    public List<string> Roles { get; set; } = new();
    public List<string> Scopes { get; set; } = new();
    public string? TenantId { get; set; }
    public string? SessionId { get; set; }
    public Dictionary<string, object> AdditionalClaims { get; set; } = new();

    public ClaimsPrincipal ToClaimsPrincipal()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Subject),
            new(ClaimTypes.Name, Name ?? string.Empty),
            new(ClaimTypes.Email, Email ?? string.Empty)
        };

        if (!string.IsNullOrEmpty(Username))
        {
            claims.Add(new(ClaimTypes.GivenName, Username));
        }

        if (!string.IsNullOrEmpty(Picture))
        {
            claims.Add(new("picture", Picture));
        }

        if (!string.IsNullOrEmpty(Locale))
        {
            claims.Add(new("locale", Locale));
        }

        if (!string.IsNullOrEmpty(ZoneInfo))
        {
            claims.Add(new("zoneinfo", ZoneInfo));
        }

        claims.Add(new("email_verified", EmailVerified.ToString().ToLower()));
        claims.Add(new("phone_number_verified", PhoneNumberVerified.ToString().ToLower()));

        if (!string.IsNullOrEmpty(PhoneNumber))
        {
            claims.Add(new(ClaimTypes.MobilePhone, PhoneNumber));
        }

        if (!string.IsNullOrEmpty(Address))
        {
            claims.Add(new(ClaimTypes.StreetAddress, Address));
        }

        foreach (var role in Roles)
        {
            claims.Add(new(ClaimTypes.Role, role));
        }

        foreach (var scope in Scopes)
        {
            claims.Add(new("scope", scope));
        }

        if (!string.IsNullOrEmpty(TenantId))
        {
            claims.Add(new("tenant_id", TenantId));
        }

        if (!string.IsNullOrEmpty(SessionId))
        {
            claims.Add(new("sid", SessionId));
        }

        foreach (var claim in AdditionalClaims)
        {
            claims.Add(new(claim.Key, claim.Value.ToString() ?? string.Empty));
        }

        var identity = new ClaimsIdentity(claims, "oidc");
        return new ClaimsPrincipal(identity);
    }
}
