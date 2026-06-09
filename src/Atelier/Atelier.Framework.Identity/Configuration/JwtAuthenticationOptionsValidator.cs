using System.Text;
using Atelier.Framework.Attributes;
using Microsoft.Extensions.Options;

namespace Atelier.Framework.Identity.Configuration;

[ContractAttribute("JwtAuthenticationOptionsValidator", Version = "1.0")]
public sealed class JwtAuthenticationOptionsValidator : IValidateOptions<JwtAuthenticationOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtAuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            failures.Add("Jwt:Issuer must be set to a service-specific value.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add("Jwt:Audience must be set to a service-specific value.");
        }

        if (!options.AllowInsecureValidation)
        {
            var disabled = new List<string>();
            if (!options.ValidateIssuer)
            {
                disabled.Add(nameof(options.ValidateIssuer));
            }
            if (!options.ValidateAudience)
            {
                disabled.Add(nameof(options.ValidateAudience));
            }
            if (!options.ValidateLifetime)
            {
                disabled.Add(nameof(options.ValidateLifetime));
            }
            if (!options.ValidateSigningKey)
            {
                disabled.Add(nameof(options.ValidateSigningKey));
            }

            if (disabled.Count > 0)
            {
                failures.Add(
                    $"Jwt has insecure validation toggles disabled: {string.Join(", ", disabled)}. Set AllowInsecureValidation only in development.");
            }
        }

        if (options.ValidateSigningKey)
        {
            var keyByteLength = string.IsNullOrEmpty(options.SigningKey)
                ? 0
                : Encoding.UTF8.GetByteCount(options.SigningKey);

            if (keyByteLength < 32)
            {
                failures.Add("Jwt:SigningKey must be at least 32 bytes (256 bits) for HMAC-SHA256.");
            }
        }

        if (failures.Count > 0)
        {
            return ValidateOptionsResult.Fail(failures);
        }

        return ValidateOptionsResult.Success;
    }
}
