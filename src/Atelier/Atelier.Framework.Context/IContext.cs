using Atelier.Framework.Context;
using Atelier.Framework.Context.Extensions;
namespace Atelier.Framework.Context
{
    public interface IContext
    {

        public string ContextId { get; }
        public string Name { get; }
        public IContext? Parent { get; }

        public ContextScope Scope { get; }
        public ContextLifecycle Lifecycle { get; }
        public DateTime CreatedAt { get; }
        public DateTime? ExpiresAt { get; set; }

        public string? ServiceId { get; set; }
        public string? DomainId { get; set; }
        public string? CorrelationId { get; set; }
        public string? TraceId { get; set; }
        public string? SpanId { get; set; }
        public string? ParentSpanId { get; set; }
        public IReadOnlyDictionary<string, string> ServiceMetadata { get; }

        public IContextExtensionRegistry Extensions { get; }

        public IReadOnlyList<IContext> Children { get; }
        public ContextStatus Status { get; }
        public IReadOnlyDictionary<string, object> Results { get; }

        public bool IsCompileTime { get; }
        public bool IsRuntime { get; }
        public IReadOnlyDictionary<string, Type> CompileTimeTypes { get; }

        public AuthorizationContext? Authorization { get; set; }

        public ContextScopeLimiter? ScopeLimiter { get; set; }

        public IReadOnlyDictionary<string, string> Data { get; }
        public IReadOnlyDictionary<string, string> AdditionalData { get; }

        public bool TryGetValue(string key, out string value);
        public IReadOnlyDictionary<string, string> GetAllValues();
        public void AddValue(string key, string value);

        public IContext CreateChild(string name, ContextScope scope);
        public void SetServiceMetadata(string key, string value);
        public void AddResult(string key, object result);
        public T? GetOutcome<T>(string key);
        public bool HasResult(string key);
        public IContext EvolveToRuntime();
        public void UpdateStatus(ContextStatus status);
        public void UpdateLifecycle(ContextLifecycle lifecycle);
        public void AddCompileTimeType(string key, Type type);

        public IContext WithAuthorization(AuthorizationContext authorization);
        public IContext InheritAuthorization();
        public bool HasAuthorization();
        public bool IsAuthorized(string permission);
        public bool IsAuthorizedForRole(string role);

        public IContext WithScopeLimiter(ContextScopeLimiter limiter);
        public IContext InheritScopeLimiter();
        public bool HasScopeLimiter();
        public bool IsDataKeyAllowed(string key);
        public bool IsOperationAllowed(string operation);
        public bool IsScopeAllowed(ContextScope scope);
        public T? GetScopeConstraint<T>(string name);
    }
}
