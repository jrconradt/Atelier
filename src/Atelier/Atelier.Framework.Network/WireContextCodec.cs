using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Atelier.Framework.Context;
using Atelier.Framework.Context.Validation;
using Atelier.Framework.Attributes;

namespace Atelier.Framework.Network;

public static class WireContextCodec
{
    public const string CANONICAL_HEADER_NAME = "X-Atelier-Context";
    public const string LOWERCASE_HEADER_NAME = "x-atelier-context";

    private const int MAX_CORRELATION_TOKEN_LENGTH = 128;
    private const int MAX_WIRE_DEPTH = 32;
    private const int MAX_WIRE_JSON_BYTES = ContextSizeValidator.DEFAULT_MAX_CONTEXT_SIZE_BYTES;
    private const int MAX_BASE64_HEADER_LENGTH = ((MAX_WIRE_JSON_BYTES + 2) / 3) * 4;
    private const int MAX_AUTHORIZATION_ENTRIES = 64;
    private const int MAX_AUTHORIZATION_KEY_LENGTH = 128;
    private const int MAX_AUTHORIZATION_VALUE_LENGTH = 512;

    private static readonly JsonSerializerOptions WireOptions = new()
    {
        MaxDepth = MAX_WIRE_DEPTH,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static string RedactIdentifier(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "anonymous";
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"sha256:{Convert.ToHexString(hash, 0, 8).ToLowerInvariant()}";
    }

    public static string? Encode(IContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var authorization = context.Authorization;

        var data = new WireContextData
        {
            UserId = authorization?.UserId,
            TenantId = authorization?.TenantId,
            SessionId = authorization?.SessionId,
            TraceId = context.TraceId,
            ParentSpanId = context.ParentSpanId,
            SpanId = context.SpanId,
            CorrelationId = context.CorrelationId,
            Claims = ToStringMap(authorization?.Claims),
            Roles = ToStringMap(authorization?.Roles),
            Permissions = ToStringMap(authorization?.Permissions)
        };

        var json = JsonSerializer.Serialize(data, WireOptions);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public static IContext? Decode(string? headerValue)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return null;
        }

        if (headerValue.Length > MAX_BASE64_HEADER_LENGTH)
        {
            return null;
        }

        WireContextData? data;
        try
        {
            var bytes = Convert.FromBase64String(headerValue);
            if (bytes.Length > MAX_WIRE_JSON_BYTES)
            {
                return null;
            }

            var json = Encoding.UTF8.GetString(bytes);
            data = JsonSerializer.Deserialize<WireContextData>(json, WireOptions);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }

        if (data == null)
        {
            return null;
        }

        var context = new global::Atelier.Framework.Context.Context(
            Guid.NewGuid().ToString(),
            "wire",
            null,
            new Dictionary<string, string>());

        context.TraceId = NormalizeCorrelationToken(data.TraceId);
        context.ParentSpanId = NormalizeCorrelationToken(data.ParentSpanId);
        context.SpanId = NormalizeCorrelationToken(data.SpanId);
        context.CorrelationId = NormalizeCorrelationToken(data.CorrelationId);

        var authorization = AuthorizationContext.FromUntrustedWire(
            NormalizeCorrelationToken(data.UserId),
            NormalizeCorrelationToken(data.TenantId),
            NormalizeCorrelationToken(data.SessionId));

        AddEach(data.Claims, (key, value) => authorization.AddClaim(key, value));
        AddEach(data.Roles, (key, value) => authorization.AddRole(key, value));
        AddEach(data.Permissions, (key, value) => authorization.AddPermission(key, value));

        context.Authorization = authorization;
        return context;
    }

    public static string? NormalizeCorrelationToken(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var bounded = value.Length > MAX_CORRELATION_TOKEN_LENGTH
            ? value.Substring(0, MAX_CORRELATION_TOKEN_LENGTH)
            : value;

        var sanitized = new List<char>(bounded.Length);
        foreach (var c in bounded)
        {
            if ((c >= 'A' && c <= 'Z')
                || (c >= 'a' && c <= 'z')
                || (c >= '0' && c <= '9')
                || c == '.'
                || c == '_'
                || c == '-')
            {
                sanitized.Add(c);
            }
        }

        if (sanitized.Count == 0)
        {
            return null;
        }

        return new string(sanitized.ToArray());
    }

    public static IContext CreateUnverifiedFallback(
        string transport,
        string? traceId,
        string? correlationId,
        string? userId)
    {
        var context = new global::Atelier.Framework.Context.Context(
            Guid.NewGuid().ToString(),
            transport,
            null,
            new Dictionary<string, string>());

        context.TraceId = NormalizeCorrelationToken(traceId) ?? Guid.NewGuid().ToString();
        context.SpanId = Guid.NewGuid().ToString();
        context.CorrelationId = NormalizeCorrelationToken(correlationId);

        var principal = NormalizeCorrelationToken(userId);
        context.Authorization = AuthorizationContext.FromUntrustedWire(
            string.IsNullOrEmpty(principal) ? "anonymous" : principal);

        return context;
    }

    private static Dictionary<string, string>? ToStringMap(IReadOnlyDictionary<string, object>? source)
    {
        if (source == null
            || source.Count == 0)
        {
            return null;
        }

        var map = new Dictionary<string, string>(source.Count);
        foreach (var kvp in source)
        {
            map[kvp.Key] = kvp.Value?.ToString() ?? string.Empty;
        }

        return map;
    }

    private static void AddEach(Dictionary<string, string>? map, Action<string, string> add)
    {
        if (map == null)
        {
            return;
        }

        var added = 0;
        foreach (var kvp in map)
        {
            if (added >= MAX_AUTHORIZATION_ENTRIES)
            {
                break;
            }

            if (string.IsNullOrEmpty(kvp.Key)
                || kvp.Key.Length > MAX_AUTHORIZATION_KEY_LENGTH)
            {
                continue;
            }

            var value = kvp.Value ?? string.Empty;
            var boundedValue = value.Length > MAX_AUTHORIZATION_VALUE_LENGTH
                ? value.Substring(0, MAX_AUTHORIZATION_VALUE_LENGTH)
                : value;

            add(kvp.Key, boundedValue);
            added++;
        }
    }
}

[Contract("WireContextData", Version = "1.0", Namespace = "Framework.Network")]
public sealed class WireContextData
{
    public string? UserId { get; set; }
    public string? SessionId { get; set; }
    public string? TenantId { get; set; }
    public string? TraceId { get; set; }
    public string? ParentSpanId { get; set; }
    public string? SpanId { get; set; }
    public string? CorrelationId { get; set; }
    public Dictionary<string, string>? Claims { get; set; }
    public Dictionary<string, string>? Roles { get; set; }
    public Dictionary<string, string>? Permissions { get; set; }
}
