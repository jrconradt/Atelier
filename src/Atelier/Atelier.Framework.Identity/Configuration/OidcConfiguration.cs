using Atelier.Framework.Attributes;
using System.ComponentModel.DataAnnotations;

namespace Atelier.Framework.Identity.Configuration;

[ContractAttribute("OidcConfiguration", Version = "1.0")]
public class OidcConfiguration
{
    public bool EnableOidc { get; set; } = true;

    public string DefaultProvider { get; set; } = string.Empty;

    public Dictionary<string, OidcProviderConfiguration> Providers { get; set; } = new();

    public bool EnableTokenValidation { get; set; } = true;

    public bool EnableAutomaticTokenRefresh { get; set; } = true;

    public List<string> ExcludedPaths { get; set; } = ["/health", "/metrics", "/swagger"];

    public bool RequireHttps { get; set; } = true;

    public OidcProviderConfiguration? GetProvider(string providerName)
    {
        ArgumentNullException.ThrowIfNull(providerName);
        return Providers.TryGetValue(providerName, out var provider) ? provider : null;
    }

    public OidcProviderConfiguration GetDefaultProvider()
    {
        if (string.IsNullOrEmpty(DefaultProvider) || !Providers.ContainsKey(DefaultProvider))
        {
            throw new InvalidOperationException("No default OIDC provider configured");
        }

        return Providers[DefaultProvider];
    }
}
