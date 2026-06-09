using Atelier.Framework.Attributes;
using System.Text.Json.Serialization;

namespace Atelier.Framework.Identity.Configuration;

[ContractAttribute("JwtAuthenticationOptions", Version = "1.0")]
public class JwtAuthenticationOptions
{
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;

    [JsonIgnore]
    public string SigningKey { get; set; } = string.Empty;
    public bool ValidateIssuer { get; set; } = true;
    public bool ValidateAudience { get; set; } = true;
    public bool ValidateLifetime { get; set; } = true;
    public bool ValidateSigningKey { get; set; } = true;
    public bool AllowInsecureValidation { get; set; }
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromHours(1);
}
