using Atelier.Framework.Context;

namespace Atelier.Framework.Context
{
    public static class ContextExtensions
    {
        public static IContext ForCrossOfferingOperation(
            this IContext context,
            string targetOfferingId,
            string targetDomainId,
            string operationName)
        {
            var childContext = context.CreateChild(operationName, ContextScope.Service);
            childContext.ServiceId = targetOfferingId;
            childContext.DomainId = targetDomainId;

            childContext.SetServiceMetadata(
                "cross-offering:source-offering",
                context.ServiceId ?? "unknown");
            childContext.SetServiceMetadata(
                "cross-offering:target-offering",
                targetOfferingId);
            childContext.SetServiceMetadata(
                "cross-offering:operation",
                operationName);
            childContext.SetServiceMetadata(
                "cross-offering:timestamp",
                DateTime.UtcNow.ToString("O"));

            return childContext;
        }
        public static IContext ForDomainOperation(
            this IContext context,
            string domainId,
            string operationName)
        {
            var childContext = context.CreateChild(operationName, ContextScope.Domain);
            childContext.DomainId = domainId;

            childContext.SetServiceMetadata(
                "domain:operation",
                operationName);
            childContext.SetServiceMetadata(
                "domain:timestamp",
                DateTime.UtcNow.ToString("O"));

            return childContext;
        }
        public static IContext AddResultAndNotify(
            this IContext context,
            string key,
            object result)
        {
            context.AddResult(key, result);

            if (context.Parent != null)
            {
                var canonicalKey = $"child:{context.ContextId}:{key}";
                if (!context.Parent.HasResult(canonicalKey))
                {
                    context.Parent.AddResult(canonicalKey, result);
                }
            }

            return context;
        }
        public static T? GetResultFromHierarchy<T>(this IContext context, string key)
        {

            if (context.HasResult(key))
            {
                return context.GetOutcome<T>(key);
            }

            foreach (var child in context.Children)
            {
                if (child.HasResult(key))
                {
                    return child.GetOutcome<T>(key);
                }
            }

            return default;
        }
        public static bool IsReadyForExecution(this IContext context)
        {
            return context.Lifecycle == ContextLifecycle.Active &&
                   context.Status == ContextStatus.Ready &&
                   context.IsRuntime;
        }
        public static bool IsCompileTimePhase(this IContext context)
        {
            return context.IsCompileTime &&
                   context.Lifecycle == ContextLifecycle.Creating;
        }
        public static Dictionary<string, object> GetAllResultsFromHierarchy(this IContext context)
        {
            var results = new Dictionary<string, object>();

            foreach (var kvp in context.Results)
            {
                results[$"{context.ContextId}:{kvp.Key}"] = kvp.Value;
            }

            foreach (var child in context.Children)
            {
                foreach (var kvp in child.Results)
                {
                    results[$"{child.ContextId}:{kvp.Key}"] = kvp.Value;
                }
            }

            return results;
        }
        public static bool HasRequiredServiceMetadata(this IContext context, params string[] requiredKeys)
        {
            foreach (var key in requiredKeys)
            {
                if (!context.ServiceMetadata.ContainsKey(key))
                {
                    return false;
                }
            }
            return true;
        }
        public static string? GetServiceMetadataWithFallback(this IContext context, string key)
        {
            var current = context;
            while (current != null)
            {
                if (current.ServiceMetadata.TryGetValue(key, out var value))
                {
                    return value;
                }

                current = current.Parent;
            }

            return null;
        }

        public static IContext WithAuthorization(
            this IContext context,
            string? userId = null,
            string? tenantId = null,
            string? sessionId = null)
        {
            var auth = AuthorizationContext.Create(userId, tenantId, sessionId);
            return context.WithAuthorization(auth);
        }
        public static IContext WithInheritedAuthorization(this IContext context)
        {
            return context.InheritAuthorization();
        }
        public static bool HasValidAuthorization(this IContext context)
        {
            return context.HasAuthorization();
        }
        public static bool IsAuthorizedForAll(this IContext context, params string[] permissions)
        {
            return permissions.All(context.IsAuthorized);
        }
        public static bool IsAuthorizedForAny(this IContext context, params string[] permissions)
        {
            return permissions.Any(context.IsAuthorized);
        }
        public static IContext WithScopeLimiter(this IContext context, ContextScope scope)
        {
            var limiter = ContextScopeLimiter.ForScope(scope);
            return context.WithScopeLimiter(limiter);
        }
        public static IContext WithInheritedScopeLimiter(this IContext context)
        {
            return context.InheritScopeLimiter();
        }
        public static bool HasScopeLimitations(this IContext context)
        {
            return context.HasScopeLimiter();
        }
        public static bool CanAddData(this IContext context, string key)
        {
            return context.IsDataKeyAllowed(key);
        }
        public static bool CanPerformOperation(this IContext context, string operation)
        {
            return context.IsOperationAllowed(operation);
        }
        public static bool CanCreateChildScope(this IContext context, ContextScope scope)
        {
            return context.IsScopeAllowed(scope);
        }
        public static Dictionary<string, string> GetFilteredData(this IContext context)
        {
            var allData = context.GetAllValues();
            var limiter = context.ScopeLimiter;

            if (limiter != null
                && limiter.IsAllowAll
                && limiter.AllowedDataKeys.Count == 0
                && limiter.BlockedDataKeys.Count == 0)
            {
                if (context is Context
                    && allData is Dictionary<string, string> merged)
                {
                    return merged;
                }

                return new Dictionary<string, string>(allData);
            }

            var filteredData = new Dictionary<string, string>();

            foreach (var kvp in allData)
            {
                if (context.IsDataKeyAllowed(kvp.Key))
                {
                    filteredData[kvp.Key] = kvp.Value;
                }
            }

            return filteredData;
        }
        public static Dictionary<string, object> GetScopeConstraints(this IContext context)
        {
            var constraints = new Dictionary<string, object>();

            if (context.ScopeLimiter != null)
            {
                foreach (var kvp in context.ScopeLimiter.ScopeConstraints)
                {
                    constraints[kvp.Key] = kvp.Value;
                }
            }

            return constraints;
        }

        public static IContext WithRestrictions(
            this IContext context,
            string[]? allowedDataKeys = null,
            string[]? blockedDataKeys = null,
            string[]? allowedOperations = null,
            string[]? blockedOperations = null,
            ContextScope[]? allowedScopes = null,
            ContextScope[]? blockedScopes = null)
        {
            var limiter = ContextScopeLimiter.Create();

            if (allowedDataKeys != null)
            {
                limiter.AllowDataKeys(allowedDataKeys);
            }

            if (blockedDataKeys != null)
            {
                limiter.BlockDataKeys(blockedDataKeys);
            }

            if (allowedOperations != null)
            {
                limiter.AllowOperations(allowedOperations);
            }

            if (blockedOperations != null)
            {
                limiter.BlockOperations(blockedOperations);
            }

            if (allowedScopes != null)
            {
                limiter.AllowScopes(allowedScopes);
            }

            if (blockedScopes != null)
            {
                limiter.BlockScopes(blockedScopes);
            }

            return context.WithScopeLimiter(limiter);
        }

        public static ContextSnapshot CreateSnapshot(this IContext context)
        {
            return new ContextSnapshot
            {
                ContextId = context.ContextId,
                Name = context.Name,
                Scope = context.Scope,
                Lifecycle = context.Lifecycle,
                CreatedAt = context.CreatedAt,
                ExpiresAt = context.ExpiresAt,
                ServiceId = context.ServiceId,
                DomainId = context.DomainId,
                CorrelationId = context.CorrelationId,
                TraceId = context.TraceId,
                SpanId = context.SpanId,
                ParentSpanId = context.ParentSpanId,
                ServiceMetadata = context.ServiceMetadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                Status = context.Status,
                Results = context.Results.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                Data = context.Data.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                AdditionalData = context.AdditionalData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                SnapshotTakenAt = DateTime.UtcNow
            };
        }
    }
}
