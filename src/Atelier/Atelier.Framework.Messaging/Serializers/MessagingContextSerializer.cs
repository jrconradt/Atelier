using System.Text.Json;
using System.Text.Json.Serialization;
using Atelier.Framework.Context;
using Atelier.Framework.Context.Validation;
using Atelier.Framework.Infrastructure;

using Atelier.Framework.Outcomes;
namespace Atelier.Framework.Messaging.Serializers
{
        public class MessagingContextSerializer : IContextSerializer
    {
        private const int CURRENT_VERSION = 1;
        private const int MAX_SERIALIZED_SIZE_BYTES = 64 * 1024;
        private const int WIRE_MAX_DEPTH = 32;

        private readonly JsonSerializerOptions _jsonOptions;

        public MessagingContextSerializer()
        {
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = false,
                MaxDepth = WIRE_MAX_DEPTH
            };
        }

        public string Serialize(IContext context)
        {
            if (context == null || context.Data == null)
            {
                return string.Empty;
            }

            var envelope = new ContextEnvelope
            {
                Version = CURRENT_VERSION,
                ContextId = context.ContextId,
                Name = context.Name,
                Scope = context.Scope,
                Lifecycle = context.Lifecycle,
                CreatedAt = context.CreatedAt,
                ServiceId = context.ServiceId,
                DomainId = context.DomainId,
                CorrelationId = context.CorrelationId,
                TraceId = context.TraceId,
                SpanId = context.SpanId,
                ParentSpanId = context.ParentSpanId,
                Status = context.Status,
                IsCompileTime = context.IsCompileTime,
                IsRuntime = context.IsRuntime
            };

            envelope.Data = context.GetFilteredData();

            envelope.Results = context.Results
                .Where(r => r.Value != null)
                .ToDictionary(r => r.Key, r => WrapResultValue(r.Value!));

            if (context.Authorization != null)
            {
                envelope.Authorization = new AuthorizationSummary
                {
                    UserId = context.Authorization.UserId,
                    TenantId = context.Authorization.TenantId,
                    SessionId = context.Authorization.SessionId,
                    IsInherited = context.Authorization.IsInherited,
                    IsValid = context.Authorization.IsValid(),
                    PermissionsCount = context.Authorization.Permissions.Count,
                    RolesCount = context.Authorization.Roles.Count
                };
            }

            if (context.ScopeLimiter != null)
            {
                envelope.ScopeLimiter = new ScopeLimiterSummary
                {
                    AllowedDataKeysCount = context.ScopeLimiter.AllowedDataKeys.Count,
                    BlockedDataKeysCount = context.ScopeLimiter.BlockedDataKeys.Count,
                    AllowedOperationsCount = context.ScopeLimiter.AllowedOperations.Count,
                    BlockedOperationsCount = context.ScopeLimiter.BlockedOperations.Count,
                    AllowedScopesCount = context.ScopeLimiter.AllowedScopes.Count,
                    BlockedScopesCount = context.ScopeLimiter.BlockedScopes.Count
                };
            }

            var json = JsonSerializer.Serialize(envelope, _jsonOptions);

            var serializedSizeBytes = System.Text.Encoding.UTF8.GetByteCount(json);
            if (serializedSizeBytes > MAX_SERIALIZED_SIZE_BYTES)
            {
                var validation = ContextSizeValidator.ValidateFieldSizes(
                    context,
                    MAX_SERIALIZED_SIZE_BYTES,
                    serializedSizeBytes);
                throw new InvalidOperationException(
                    $"Context exceeds messaging size limits. Context ID: {context.ContextId}. " +
                    $"Violations: {string.Join("; ", validation.Violations)}");
            }

            return json;
        }

        public IContext Deserialize(string serialized)
        {
            if (string.IsNullOrWhiteSpace(serialized))
            {
                return Context.Context.Empty;
            }

            var outcome = TryDeserializeInternal(serialized);
            if (!outcome.IsSuccess)
            {
                throw new InvalidOperationException("Failed to deserialize context");
            }

            return outcome.Data;
        }

        public bool TryDeserialize(string serialized, out IContext? context)
        {
            var outcome = TryDeserializeInternal(serialized);
            context = outcome.IsSuccess ? outcome.Data : null;
            return outcome.IsSuccess;
        }

        private Outcome<IContext> TryDeserializeInternal(string serialized)
        {
            if (string.IsNullOrWhiteSpace(serialized))
            {
                return Outcome<IContext>.Success(Context.Context.Empty);
            }

            var serializedSizeBytes = System.Text.Encoding.UTF8.GetByteCount(serialized);
            if (serializedSizeBytes > MAX_SERIALIZED_SIZE_BYTES)
            {
                return Outcome<IContext>.Failure();
            }

            ContextEnvelope? envelope;
            try
            {
                envelope = JsonSerializer.Deserialize<ContextEnvelope>(serialized, _jsonOptions);
            }
            catch (JsonException)
            {
                return Outcome<IContext>.Failure();
            }

            if (envelope == null)
            {
                return Outcome<IContext>.Failure();
            }

            if (envelope.Version > CURRENT_VERSION)
            {
                return Outcome<IContext>.Failure();
            }

            var deserializedContext = new CompositeContext(
                envelope.ContextId,
                envelope.Name,
                null,
                envelope.Data ?? new Dictionary<string, string>());

            deserializedContext.Scope = envelope.Scope;
            deserializedContext.Lifecycle = envelope.Lifecycle;
            deserializedContext.CreatedAt = envelope.CreatedAt;
            deserializedContext.ServiceId = envelope.ServiceId;
            deserializedContext.DomainId = envelope.DomainId;
            deserializedContext.CorrelationId = envelope.CorrelationId;
            deserializedContext.TraceId = envelope.TraceId;
            deserializedContext.SpanId = envelope.SpanId;
            deserializedContext.ParentSpanId = envelope.ParentSpanId;
            deserializedContext.Status = envelope.Status;
            deserializedContext.IsCompileTime = envelope.IsCompileTime;

            if (envelope.Results != null)
            {
                foreach (var result in envelope.Results)
                {
                    var unwrapped = UnwrapResultValue(result.Value);
                    if (unwrapped == null)
                    {
                        continue;
                    }

                    deserializedContext.AddResult(result.Key, unwrapped);
                }
            }

            if (envelope.Authorization != null)
            {
                var wireAuthorization = AuthorizationContext.FromUntrustedWire(
                    envelope.Authorization.UserId,
                    envelope.Authorization.TenantId,
                    envelope.Authorization.SessionId);
                wireAuthorization.IsInherited = envelope.Authorization.IsInherited;
                deserializedContext.Authorization = wireAuthorization;
            }

            return Outcome<IContext>.Success(deserializedContext);
        }

        private static object WrapResultValue(object value)
        {
            var valueType = value.GetType();
            return new TypedResultEnvelope
            {
                Type = valueType.AssemblyQualifiedName ?? valueType.FullName ?? valueType.Name,
                Value = value
            };
        }

        private object? UnwrapResultValue(object? value)
        {
            if (value is not JsonElement element)
            {
                return value;
            }

            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty("type", out var typeProperty)
                && element.TryGetProperty("value", out var valueProperty)
                && typeProperty.ValueKind == JsonValueKind.String)
            {
                return ReconstructTypedValue(typeProperty, valueProperty);
            }

            return ReadJsonScalar(element);
        }

        private object? ReconstructTypedValue(JsonElement typeProperty, JsonElement valueProperty)
        {
            var resolved = SafeTypeResolver.Resolve(typeProperty.GetString());
            if (resolved == null
                || !IsAllowedResultType(resolved))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize(valueProperty.GetRawText(), resolved, _jsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }

        private static bool IsAllowedResultType(Type type)
        {
            return type.IsPrimitive
                || type == typeof(string)
                || type == typeof(DateTime)
                || type == typeof(DateTimeOffset)
                || type == typeof(TimeSpan)
                || type == typeof(decimal)
                || type == typeof(Guid)
                || type.IsEnum
                || (type.IsArray && type.GetElementType()?.IsPrimitive == true);
        }

        private static object? ReadJsonScalar(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                {
                    return element.GetString();
                }
                case JsonValueKind.True:
                {
                    return true;
                }
                case JsonValueKind.False:
                {
                    return false;
                }
                case JsonValueKind.Number:
                {
                    if (element.TryGetInt64(out var longValue))
                    {
                        return longValue;
                    }

                    return element.GetDouble();
                }
                default:
                {
                    return null;
                }
            }
        }

        private sealed class TypedResultEnvelope
        {
            public string Type { get; set; } = string.Empty;

            public object? Value { get; set; }
        }
    }
}
