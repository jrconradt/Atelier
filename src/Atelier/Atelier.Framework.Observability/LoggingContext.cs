using Atelier.Framework.Context;

namespace Atelier.Framework.Observability
{
    public sealed class LoggingContext
    {
        public IContext Context { get; }
        public string Message { get; }
        public Exception? Exception { get; }
        public IDictionary<string, object> Values { get; }
        public LogLevel Level { get; }
        public DateTime Timestamp { get; }

        public LoggingContext(
            IContext context,
            string message,
            Exception? exception,
            IDictionary<string, object> values,
            LogLevel level = LogLevel.Information)
        {
            ArgumentNullException.ThrowIfNull(context);
            Context = context;
            Message = SensitiveValueRedactor.RedactText(message);
            Exception = exception;

            Values = SensitiveValueRedactor.RedactInPlace(
                values != null
                    ? new Dictionary<string, object>(values)
                    : new Dictionary<string, object>());
            Level = level;
            Timestamp = DateTime.UtcNow;
        }
        public Dictionary<string, object> GetContextMetadata()
        {
            var metadata = new Dictionary<string, object>
            {
                ["ContextId"] = Context.ContextId,
                ["ContextName"] = Context.Name,
                ["Scope"] = Context.Scope.ToString(),
                ["Lifecycle"] = Context.Lifecycle.ToString(),
                ["Status"] = Context.Status.ToString(),
                ["IsCompileTime"] = Context.IsCompileTime,
                ["IsRuntime"] = Context.IsRuntime,
                ["CreatedAt"] = Context.CreatedAt,
                ["ChildrenCount"] = Context.Children.Count
            };

            if (!string.IsNullOrEmpty(Context.ServiceId))
            {
                metadata["ServiceId"] = Context.ServiceId;
            }

            if (!string.IsNullOrEmpty(Context.DomainId))
            {
                metadata["DomainId"] = Context.DomainId;
            }

            if (!string.IsNullOrEmpty(Context.CorrelationId))
            {
                metadata["CorrelationId"] = Context.CorrelationId;
            }

            if (!string.IsNullOrEmpty(Context.TraceId))
            {
                metadata["TraceId"] = Context.TraceId;
            }

            if (!string.IsNullOrEmpty(Context.SpanId))
            {
                metadata["SpanId"] = Context.SpanId;
            }

            if (!string.IsNullOrEmpty(Context.ParentSpanId))
            {
                metadata["ParentSpanId"] = Context.ParentSpanId;
            }

            if (Context.Authorization != null)
            {
                metadata["UserId"] = Context.Authorization.UserId ?? "anonymous";
                metadata["TenantId"] = Context.Authorization.TenantId ?? "default";
                metadata["SessionId"] = Context.Authorization.SessionId ?? "none";
                metadata["AuthorizationInherited"] = Context.Authorization.IsInherited;
                metadata["AuthorizationValid"] = Context.Authorization.IsValid();
            }

            if (Context.ScopeLimiter != null)
            {
                metadata["HasScopeLimiter"] = true;
                metadata["AllowedDataKeys"] = Context.ScopeLimiter.AllowedDataKeys.Count;
                metadata["BlockedDataKeys"] = Context.ScopeLimiter.BlockedDataKeys.Count;
                metadata["AllowedOperations"] = Context.ScopeLimiter.AllowedOperations.Count;
                metadata["BlockedOperations"] = Context.ScopeLimiter.BlockedOperations.Count;
            }

            var filteredData = Context.GetFilteredData();
            foreach (var kvp in filteredData)
            {
                metadata[$"ContextData.{kvp.Key}"] = kvp.Value;
            }

            foreach (var kvp in Context.Results)
            {
                metadata[$"Outcome.{kvp.Key}"] = kvp.Value;
            }

            return new Dictionary<string, object>(SensitiveValueRedactor.RedactInPlace(metadata));
        }
        public const int MaxHierarchyDepth = 10;

        public Dictionary<string, object> GetHierarchyMetadata()
        {
            var hierarchy = new Dictionary<string, object>();
            var current = Context;
            var depth = 0;

            while (current != null
                   && depth < MaxHierarchyDepth)
            {
                hierarchy[$"Level{depth}.Id"] = current.ContextId;
                hierarchy[$"Level{depth}.Name"] = current.Name;
                hierarchy[$"Level{depth}.Scope"] = current.Scope.ToString();
                hierarchy[$"Level{depth}.Status"] = current.Status.ToString();
                hierarchy[$"Level{depth}.ResultsCount"] = current.Results.Count;

                current = current.Parent;
                depth++;
            }

            hierarchy["MaxDepth"] = depth;
            hierarchy["HierarchyTruncated"] = current != null;
            return hierarchy;
        }
        public Dictionary<string, object> GetAuthorizationSummary()
        {
            var summary = new Dictionary<string, object>();

            if (Context.Authorization == null)
            {
                summary["HasAuthorization"] = false;
                return summary;
            }

            var auth = Context.Authorization;
            summary["HasAuthorization"] = true;
            summary["UserId"] = auth.UserId ?? "anonymous";
            summary["TenantId"] = auth.TenantId ?? "default";
            summary["SessionId"] = auth.SessionId ?? "none";
            summary["IsInherited"] = auth.IsInherited;
            summary["IsValid"] = auth.IsValid();
            summary["PermissionsCount"] = auth.Permissions.Count;
            summary["RolesCount"] = auth.Roles.Count;
            summary["ClaimsCount"] = auth.Claims.Count;

            summary["HasPermissions"] = auth.Permissions.Count > 0;
            summary["HasRoles"] = auth.Roles.Count > 0;
            summary["HasClaims"] = auth.Claims.Count > 0;

            return new Dictionary<string, object>(SensitiveValueRedactor.RedactInPlace(summary));
        }
        public Dictionary<string, object> GetScopeLimiterSummary()
        {
            var summary = new Dictionary<string, object>();

            if (Context.ScopeLimiter == null)
            {
                summary["HasScopeLimiter"] = false;
                return summary;
            }

            var limiter = Context.ScopeLimiter;
            summary["HasScopeLimiter"] = true;
            summary["AllowedDataKeysCount"] = limiter.AllowedDataKeys.Count;
            summary["BlockedDataKeysCount"] = limiter.BlockedDataKeys.Count;
            summary["AllowedOperationsCount"] = limiter.AllowedOperations.Count;
            summary["BlockedOperationsCount"] = limiter.BlockedOperations.Count;
            summary["AllowedScopesCount"] = limiter.AllowedScopes.Count;
            summary["BlockedScopesCount"] = limiter.BlockedScopes.Count;
            summary["ConstraintsCount"] = limiter.ScopeConstraints.Count;

            return summary;
        }
    }
}




