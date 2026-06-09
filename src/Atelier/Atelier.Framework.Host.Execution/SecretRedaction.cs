using Atelier.Framework.Observability;

namespace Atelier.Framework.Host.Execution;

public static class SecretRedaction
{
    public const string RedactedPlaceholder = SensitiveValueRedactor.RedactedPlaceholder;

    public static bool IsSensitiveKey(string key)
    {
        return SensitiveValueRedactor.IsSensitiveKey(key);
    }

    public static (string Key, object Value)[] Redact(
        IReadOnlyCollection<string> secretClaims,
        params ReadOnlySpan<(string Key, object Value)> values)
    {
        var declaredSecrets = new HashSet<string>(
            secretClaims ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);

        var redacted = new (string Key, object Value)[values.Length];
        for (var index = 0; index < values.Length; index++)
        {
            var pair = values[index];
            if (declaredSecrets.Contains(pair.Key)
                || IsSensitiveKey(pair.Key))
            {
                redacted[index] = (pair.Key, RedactedPlaceholder);
            }
            else
            {
                redacted[index] = pair;
            }
        }

        return redacted;
    }
}
