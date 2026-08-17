using System.Collections.Generic;
using System.Text.Json.Serialization;
using Atelier.Framework.Attributes;

namespace Atelier.Framework.Identity.Configuration;

[ContractAttribute("OidcServerOptions", Version = "1.0")]
public class OidcServerOptions
{
    public string Issuer { get; set; } = "http://localhost:5001";
    public List<OidcServerClientOption> Clients { get; set; } = new();
    public List<OidcServerUserOption> Users { get; set; } = new();
}

[ContractAttribute("OidcServerClientOption", Version = "1.0")]
public class OidcServerClientOption
{
    public string ClientId { get; set; } = string.Empty;

    [JsonIgnore]
    public string ClientSecret { get; set; } = string.Empty;

    public List<string> Scopes { get; set; } = new();
}

[ContractAttribute("OidcServerUserOption", Version = "1.0")]
public class OidcServerUserOption
{
    public string Username { get; set; } = string.Empty;

    [JsonIgnore]
    public string Password { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;
    public List<string> Scopes { get; set; } = new();
    public List<string> Roles { get; set; } = new();
}
