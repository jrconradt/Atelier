using Atelier.Framework.Attributes;

namespace Atelier.Facilities.Cache;

[Contract("CacheKey", Version = "1.0", Namespace = "Facilities.Cache")]
public class CacheKey
{
    public required string Key { get; init; }
    public string? Namespace { get; init; }
}

[Contract("CacheValue", Version = "1.0", Namespace = "Facilities.Cache")]
public class CacheValue
{
    public required string Value { get; init; }
    public TimeSpan? Ttl { get; init; }
}

[Contract("CacheLookup", Version = "1.0", Namespace = "Facilities.Cache")]
public class CacheLookup
{
    public bool Found { get; init; }
    public CacheValue? Value { get; init; }
}

public static class CacheKeyExtensions
{
    public static string Composite(this CacheKey key)
    {
        var encodedKey = EncodeSegment(key.Key);
        return string.IsNullOrEmpty(key.Namespace) ? encodedKey : $"{EncodeSegment(key.Namespace)}:{encodedKey}";
    }

    public static string Composite(
        this CacheKey key,
        string tenantScope)
    {
        return $"{EncodeSegment(tenantScope)}:{key.Composite()}";
    }

    public static IReadOnlyList<string> DecomposeSegments(string composite)
    {
        ArgumentNullException.ThrowIfNull(composite);
        return composite
            .Split(':')
            .Select(DecodeSegment)
            .ToArray();
    }

    private static string EncodeSegment(string segment)
    {
        return segment
            .Replace("%", "%25", StringComparison.Ordinal)
            .Replace(":", "%3A", StringComparison.Ordinal);
    }

    private static string DecodeSegment(string segment)
    {
        return segment
            .Replace("%3A", ":", StringComparison.Ordinal)
            .Replace("%25", "%", StringComparison.Ordinal);
    }
}
