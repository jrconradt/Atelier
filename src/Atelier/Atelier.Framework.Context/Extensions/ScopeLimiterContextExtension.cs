namespace Atelier.Framework.Context.Extensions
{
    public class ScopeLimiterContextExtension : IContextExtension
    {
        private readonly ContextScopeLimiter _limiter;

        public ScopeLimiterContextExtension()
            : this(ContextScopeLimiter.Create())
        {
        }

        private ScopeLimiterContextExtension(ContextScopeLimiter limiter)
        {
            _limiter = limiter;
        }

        public string ExtensionName => "ScopeLimiter";

        public bool ShouldPropagateToChildren => true;

        public IReadOnlySet<string> AllowedDataKeys => _limiter.AllowedDataKeys;
        public IReadOnlySet<string> BlockedDataKeys => _limiter.BlockedDataKeys;
        public IReadOnlySet<string> AllowedOperations => _limiter.AllowedOperations;
        public IReadOnlySet<string> BlockedOperations => _limiter.BlockedOperations;
        public IReadOnlySet<ContextScope> AllowedScopes => _limiter.AllowedScopes;
        public IReadOnlySet<ContextScope> BlockedScopes => _limiter.BlockedScopes;
        public IReadOnlyDictionary<string, object> Constraints => _limiter.ScopeConstraints;

        public ScopeLimiterContextExtension AllowDataKeys(params string[] keys)
        {
            _limiter.AllowDataKeys(keys);
            return this;
        }

        public ScopeLimiterContextExtension BlockDataKeys(params string[] keys)
        {
            _limiter.BlockDataKeys(keys);
            return this;
        }

        public ScopeLimiterContextExtension AllowOperations(params string[] operations)
        {
            _limiter.AllowOperations(operations);
            return this;
        }

        public ScopeLimiterContextExtension BlockOperations(params string[] operations)
        {
            _limiter.BlockOperations(operations);
            return this;
        }

        public ScopeLimiterContextExtension AllowScopes(params ContextScope[] scopes)
        {
            _limiter.AllowScopes(scopes);
            return this;
        }

        public ScopeLimiterContextExtension BlockScopes(params ContextScope[] scopes)
        {
            _limiter.BlockScopes(scopes);
            return this;
        }

        public ScopeLimiterContextExtension AddConstraint(string name, object value)
        {
            _limiter.AddConstraint(name, value);
            return this;
        }

        public bool IsDataKeyAllowed(string key) => _limiter.IsDataKeyAllowed(key);

        public bool IsOperationAllowed(string operation) => _limiter.IsOperationAllowed(operation);

        public bool IsScopeAllowed(ContextScope scope) => _limiter.IsScopeAllowed(scope);

        public IContextExtension Clone()
        {
            return new ScopeLimiterContextExtension(_limiter.Clone());
        }
    }
}
