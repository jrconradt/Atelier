using Atelier.Framework.Attributes;
using Microsoft.Extensions.Options;

namespace Atelier.Framework.Identity.Configuration;

[ContractAttribute("OidcConfigurationValidator", Version = "1.0")]
public sealed class OidcConfigurationValidator : IValidateOptions<OidcConfiguration>
{
    private const int MINIMUM_CLIENT_SECRET_LENGTH = 32;

    public ValidateOptionsResult Validate(string? name, OidcConfiguration options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.EnableOidc)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (options.Providers.Count == 0)
        {
            failures.Add("Oidc:Providers must contain at least one provider when OIDC is enabled.");
        }

        if (!string.IsNullOrEmpty(options.DefaultProvider)
            && !options.Providers.ContainsKey(options.DefaultProvider))
        {
            failures.Add($"Oidc:DefaultProvider '{options.DefaultProvider}' is not present in Oidc:Providers.");
        }

        foreach (var pair in options.Providers)
        {
            var providerKey = pair.Key;
            var provider = pair.Value;

            if (provider is null)
            {
                failures.Add($"Oidc:Providers:{providerKey} is null.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(provider.Authority))
            {
                failures.Add($"Oidc:Providers:{providerKey}:Authority is required.");
            }
            else if (provider.RequireHttps
                     && !IsHttps(provider.Authority))
            {
                failures.Add($"Oidc:Providers:{providerKey}:Authority must use HTTPS when RequireHttps is enabled.");
            }

            if (string.IsNullOrWhiteSpace(provider.ClientId))
            {
                failures.Add($"Oidc:Providers:{providerKey}:ClientId is required.");
            }

            if (string.IsNullOrWhiteSpace(provider.ClientSecret))
            {
                failures.Add($"Oidc:Providers:{providerKey}:ClientSecret is required.");
            }
            else if (provider.ClientSecret.StartsWith("${", StringComparison.Ordinal))
            {
                failures.Add($"Oidc:Providers:{providerKey}:ClientSecret looks like an unexpanded '${{...}}' placeholder; supply the secret via environment variables or user-secrets.");
            }
            else if (provider.ClientSecret.Length < MINIMUM_CLIENT_SECRET_LENGTH)
            {
                failures.Add($"Oidc:Providers:{providerKey}:ClientSecret must be at least {MINIMUM_CLIENT_SECRET_LENGTH} characters.");
            }

            if (!string.IsNullOrEmpty(provider.RedirectUri)
                && provider.RequireHttps
                && !IsHttps(provider.RedirectUri))
            {
                failures.Add($"Oidc:Providers:{providerKey}:RedirectUri must use HTTPS when RequireHttps is enabled.");
            }

            if (!provider.AllowInsecureValidation)
            {
                var disabled = new List<string>();
                if (!provider.ValidateIssuer)
                {
                    disabled.Add(nameof(provider.ValidateIssuer));
                }
                if (!provider.ValidateAudience)
                {
                    disabled.Add(nameof(provider.ValidateAudience));
                }
                if (!provider.ValidateLifetime)
                {
                    disabled.Add(nameof(provider.ValidateLifetime));
                }
                if (!provider.ValidateIssuerSigningKey)
                {
                    disabled.Add(nameof(provider.ValidateIssuerSigningKey));
                }

                if (disabled.Count > 0)
                {
                    failures.Add(
                        $"Oidc:Providers:{providerKey} has insecure validation toggles disabled: {string.Join(", ", disabled)}. Set AllowInsecureValidation only in development.");
                }
            }
        }

        if (failures.Count > 0)
        {
            return ValidateOptionsResult.Fail(failures);
        }

        return ValidateOptionsResult.Success;
    }

    private static bool IsHttps(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps;
    }
}
