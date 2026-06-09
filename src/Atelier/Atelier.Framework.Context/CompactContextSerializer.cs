using System.Security.Cryptography;
using System.Text;

using Atelier.Framework.Context;
namespace Atelier.Framework.Context
{
    public class CompactContextSerializer : IContextSerializer
    {
        private const char PART_SEPARATOR = ';';
        private const char KEY_VALUE_SEPARATOR = '=';
        private const char PAYLOAD_MAC_SEPARATOR = '.';
        private const string TOKEN_KEY_ENVIRONMENT_VARIABLE = "ATELIER_CONTEXT_TOKEN_KEY";

        private readonly byte[] _signingKey;

        public CompactContextSerializer()
            : this(ResolveSigningKeyFromEnvironment())
        {
        }

        public CompactContextSerializer(byte[] signingKey)
        {
            if (signingKey == null)
            {
                throw new ArgumentNullException(nameof(signingKey));
            }

            if (signingKey.Length < 32)
            {
                throw new ArgumentException(
                    "Context token signing key must be at least 32 bytes (256 bits) for HMAC-SHA256.",
                    nameof(signingKey));
            }

            _signingKey = signingKey;
        }

        private static byte[] ResolveSigningKeyFromEnvironment()
        {
            var configured = Environment.GetEnvironmentVariable(TOKEN_KEY_ENVIRONMENT_VARIABLE);
            if (string.IsNullOrEmpty(configured))
            {
                throw new InvalidOperationException(
                    $"Context token signing key is not configured. Set the '{TOKEN_KEY_ENVIRONMENT_VARIABLE}' environment variable to a base64-encoded key of at least 32 bytes.");
            }

            return Convert.FromBase64String(configured);
        }

        public string Serialize(IContext context)
        {
            if (context == null
                || context.Data == null)
            {
                return string.Empty;
            }

            var parts = new List<string>
            {
                $"id{KEY_VALUE_SEPARATOR}{Uri.EscapeDataString(context.ContextId)}",
                $"name{KEY_VALUE_SEPARATOR}{Uri.EscapeDataString(context.Name)}",
                $"scope{KEY_VALUE_SEPARATOR}{(int)context.Scope}",
                $"corr{KEY_VALUE_SEPARATOR}{Uri.EscapeDataString(context.CorrelationId ?? string.Empty)}"
            };

            if (!string.IsNullOrEmpty(context.ServiceId))
            {
                parts.Add($"svc{KEY_VALUE_SEPARATOR}{Uri.EscapeDataString(context.ServiceId)}");
            }

            if (!string.IsNullOrEmpty(context.DomainId))
            {
                parts.Add($"domain{KEY_VALUE_SEPARATOR}{Uri.EscapeDataString(context.DomainId)}");
            }

            if (!string.IsNullOrEmpty(context.TraceId))
            {
                parts.Add($"trace{KEY_VALUE_SEPARATOR}{Uri.EscapeDataString(context.TraceId)}");
            }

            if (!string.IsNullOrEmpty(context.SpanId))
            {
                parts.Add($"span{KEY_VALUE_SEPARATOR}{Uri.EscapeDataString(context.SpanId)}");
            }

            if (!string.IsNullOrEmpty(context.ParentSpanId))
            {
                parts.Add($"pspan{KEY_VALUE_SEPARATOR}{Uri.EscapeDataString(context.ParentSpanId)}");
            }

            if (context.Authorization != null)
            {
                if (!string.IsNullOrEmpty(context.Authorization.UserId))
                {
                    parts.Add($"user{KEY_VALUE_SEPARATOR}{Uri.EscapeDataString(context.Authorization.UserId)}");
                }

                if (!string.IsNullOrEmpty(context.Authorization.TenantId))
                {
                    parts.Add($"tenant{KEY_VALUE_SEPARATOR}{Uri.EscapeDataString(context.Authorization.TenantId)}");
                }

                if (!string.IsNullOrEmpty(context.Authorization.SessionId))
                {
                    parts.Add($"session{KEY_VALUE_SEPARATOR}{Uri.EscapeDataString(context.Authorization.SessionId)}");
                }
            }

            var serialized = string.Join(PART_SEPARATOR, parts);
            var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(serialized));
            var mac = ComputeMac(payload);
            return $"{payload}{PAYLOAD_MAC_SEPARATOR}{mac}";
        }

        private string ComputeMac(string payload)
        {
            var mac = HMACSHA256.HashData(_signingKey, Encoding.UTF8.GetBytes(payload));
            return Convert.ToBase64String(mac);
        }

        public IContext Deserialize(string serialized)
        {
            if (string.IsNullOrWhiteSpace(serialized))
            {
                return Context.Empty;
            }

            if (!TryDeserialize(serialized, out var context))
            {
                throw new InvalidOperationException("Failed to deserialize context");
            }

            return context!;
        }

        public bool TryDeserialize(string serialized, out IContext? context)
        {
            context = null;

            try
            {
                if (string.IsNullOrWhiteSpace(serialized))
                {
                    context = Context.Empty;
                    return true;
                }

                var macIndex = serialized.IndexOf(PAYLOAD_MAC_SEPARATOR);
                if (macIndex <= 0 || macIndex == serialized.Length - 1)
                {
                    return false;
                }

                var payload = serialized.Substring(0, macIndex);
                var presentedMac = serialized.Substring(macIndex + 1);
                var expectedMac = ComputeMac(payload);

                if (!CryptographicOperations.FixedTimeEquals(
                        Encoding.UTF8.GetBytes(presentedMac),
                        Encoding.UTF8.GetBytes(expectedMac)))
                {
                    return false;
                }

                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
                var parts = decoded.Split(PART_SEPARATOR)
                    .Select(p => p.Split(KEY_VALUE_SEPARATOR, 2))
                    .Where(p => p.Length == 2)
                    .ToDictionary(
                        p => p[0],
                        p => p[1],
                        StringComparer.OrdinalIgnoreCase);

                if (!parts.TryGetValue("id", out var contextIdRaw))
                {
                    return false;
                }

                var contextId = Uri.UnescapeDataString(contextIdRaw);

                var name = parts.TryGetValue("name", out var n)
                    ? Uri.UnescapeDataString(n)
                    : "deserialized";

                var deserializedContext = new CompositeContext(
                    contextId,
                    name,
                    null,
                    new Dictionary<string, string>());

                if (parts.TryGetValue("scope", out var scopeStr)
                    && int.TryParse(scopeStr, out var scopeInt)
                    && Enum.IsDefined(typeof(ContextScope), scopeInt))
                {
                    deserializedContext.Scope = (ContextScope)scopeInt;
                }

                if (parts.TryGetValue("corr", out var corrId))
                {
                    deserializedContext.CorrelationId = Uri.UnescapeDataString(corrId);
                }

                if (parts.TryGetValue("svc", out var serviceId))
                {
                    deserializedContext.ServiceId = Uri.UnescapeDataString(serviceId);
                }

                if (parts.TryGetValue("domain", out var domainId))
                {
                    deserializedContext.DomainId = Uri.UnescapeDataString(domainId);
                }

                if (parts.TryGetValue("trace", out var traceId))
                {
                    deserializedContext.TraceId = Uri.UnescapeDataString(traceId);
                }

                if (parts.TryGetValue("span", out var spanId))
                {
                    deserializedContext.SpanId = Uri.UnescapeDataString(spanId);
                }

                if (parts.TryGetValue("pspan", out var parentSpanId))
                {
                    deserializedContext.ParentSpanId = Uri.UnescapeDataString(parentSpanId);
                }

                var hasUserId = parts.TryGetValue("user", out var userId);
                var hasTenantId = parts.TryGetValue("tenant", out var tenantId);
                var hasSessionId = parts.TryGetValue("session", out var sessionId);

                if (hasUserId || hasTenantId
                    || hasSessionId)
                {
                    deserializedContext.Authorization = AuthorizationContext.FromUntrustedWire(
                        hasUserId ? Uri.UnescapeDataString(userId!) : null,
                        hasTenantId ? Uri.UnescapeDataString(tenantId!) : null,
                        hasSessionId ? Uri.UnescapeDataString(sessionId!) : null);
                }

                deserializedContext.IsCompileTime = false;
                deserializedContext.UpdateLifecycle(ContextLifecycle.Active);

                context = deserializedContext;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
