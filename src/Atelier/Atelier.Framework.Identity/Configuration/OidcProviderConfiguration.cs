using Atelier.Framework.Attributes;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Atelier.Framework.Identity.Configuration;

[ContractAttribute("OidcProviderConfiguration", Version = "1.0")]
public class OidcProviderConfiguration
{
    [Required]
    public string Authority { get; set; } = string.Empty;

    [Required]
    public string ClientId { get; set; } = string.Empty;

    [Required]
    [JsonIgnore]
    public string ClientSecret { get; set; } = string.Empty;

    public string? RedirectUri { get; set; }

    public string? PostLogoutRedirectUri { get; set; }

    public List<string> Scopes { get; set; } = ["openid", "profile", "email"];

    public List<string> ResponseTypes { get; set; } = ["code"];

    public string? ResponseMode { get; set; }

    public bool RequireHttps { get; set; } = true;

    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromHours(1);

    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(30);

    public bool ValidateIssuer { get; set; } = true;

    public bool ValidateAudience { get; set; } = true;

    public bool ValidateLifetime { get; set; } = true;

    public bool ValidateIssuerSigningKey { get; set; } = true;

    public bool AllowInsecureValidation { get; set; }

    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromMinutes(5);

    public Dictionary<string, string> AdditionalParameters { get; set; } = new();

    public ClaimsMappingConfiguration ClaimsMapping { get; set; } = new();
}

[ContractAttribute("ClaimsMappingConfiguration", Version = "1.0")]
public class ClaimsMappingConfiguration
{
    public string SubjectClaim { get; set; } = "sub";
    public string NameClaim { get; set; } = "name";
    public string EmailClaim { get; set; } = "email";
    public string UsernameClaim { get; set; } = "preferred_username";
    public string RolesClaim { get; set; } = "roles";
    public string ScopesClaim { get; set; } = "scope";
    public string TenantIdClaim { get; set; } = "tenant_id";
    public string SessionIdClaim { get; set; } = "sid";

    public Dictionary<string, string> CustomMappings { get; set; } = new();
}
