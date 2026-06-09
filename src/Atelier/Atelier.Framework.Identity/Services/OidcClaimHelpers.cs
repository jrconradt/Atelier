using System.Text.Json;

namespace Atelier.Framework.Identity.Services;

internal static class OidcClaimHelpers
{
    public static string? GetClaimValue(
        Dictionary<string, object> claims,
        string claimName)
    {
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(claimName);

        return ResolveClaim(claims, claimName) switch
        {
            null => null,
            JsonElement element => ElementToString(element),
            var value => value.ToString()
        };
    }

    public static bool GetBooleanClaimValue(
        Dictionary<string, object> claims,
        string claimName)
    {
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(claimName);

        var resolved = ResolveClaim(claims, claimName);
        if (resolved is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (element.ValueKind == JsonValueKind.False)
            {
                return false;
            }

            return bool.TryParse(element.ToString(), out var parsedElement) && parsedElement;
        }

        return bool.TryParse(resolved?.ToString(), out var parsed) && parsed;
    }

    public static List<string> GetArrayClaimValues(
        Dictionary<string, object> claims,
        string claimName)
    {
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(claimName);

        var resolved = ResolveClaim(claims, claimName);
        if (resolved is null)
        {
            return new List<string>();
        }

        if (resolved is JsonElement element)
        {
            return ElementToList(element);
        }

        return resolved switch
        {
            string str when !string.IsNullOrEmpty(str) => str.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList(),
            List<object> list => list.Select(x => x.ToString() ?? string.Empty).ToList(),
            object[] array => array.Select(x => x.ToString() ?? string.Empty).ToList(),
            _ => new List<string>()
        };
    }

    public static Dictionary<string, object> GetAdditionalClaims(
        Dictionary<string, object> claims,
        IEnumerable<string> mappedClaimNames)
    {
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(mappedClaimNames);

        var mapped = new HashSet<string>(
            mappedClaimNames.Select(TopLevelKey),
            StringComparer.Ordinal);

        var additionalClaims = new Dictionary<string, object>();
        foreach (var claim in claims)
        {
            if (!mapped.Contains(claim.Key))
            {
                additionalClaims[claim.Key] = claim.Value;
            }
        }

        return additionalClaims;
    }

    private static string TopLevelKey(string claimName)
    {
        var separator = claimName.IndexOf('.');
        return separator < 0 ? claimName : claimName.Substring(0, separator);
    }

    private static object? ResolveClaim(
        Dictionary<string, object> claims,
        string claimName)
    {
        if (claims.TryGetValue(claimName, out var direct))
        {
            return direct;
        }

        var separator = claimName.IndexOf('.');
        if (separator < 0)
        {
            return null;
        }

        var head = claimName.Substring(0, separator);
        var tail = claimName.Substring(separator + 1);

        if (!claims.TryGetValue(head, out var container))
        {
            return null;
        }

        if (container is JsonElement element)
        {
            return NavigateElement(element, tail);
        }

        if (container is Dictionary<string, object> nested)
        {
            return ResolveClaim(nested, tail);
        }

        return null;
    }

    private static object? NavigateElement(
        JsonElement element,
        string path)
    {
        var current = element;
        var segments = path.Split('.');

        foreach (var segment in segments)
        {
            if (current.ValueKind != JsonValueKind.Object
                || !current.TryGetProperty(segment, out var next))
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    private static string? ElementToString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => element.GetString(),
            _ => element.ToString()
        };
    }

    private static List<string> ElementToList(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            var values = new List<string>();
            foreach (var item in element.EnumerateArray())
            {
                var text = ElementToString(item);
                if (text != null)
                {
                    values.Add(text);
                }
            }

            return values;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var str = element.GetString();
            return string.IsNullOrEmpty(str)
                ? new List<string>()
                : str.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        return new List<string>();
    }
}
