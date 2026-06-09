
namespace Atelier.Framework.Context
{
    public class ContextScopeLimiter
    {
        private readonly HashSet<string> _allowedDataKeys = new();
        private readonly HashSet<string> _blockedDataKeys = new();
        private readonly HashSet<string> _allowedOperations = new();
        private readonly HashSet<string> _blockedOperations = new();
        private readonly HashSet<ContextScope> _allowedScopes = new();
        private readonly HashSet<ContextScope> _blockedScopes = new();
        private readonly Dictionary<string, object> _scopeConstraints = new();
        private bool _allowAll;
        public bool IsAllowAll => _allowAll;
        public IReadOnlySet<string> AllowedDataKeys => _allowedDataKeys;
        public IReadOnlySet<string> BlockedDataKeys => _blockedDataKeys;
        public IReadOnlySet<string> AllowedOperations => _allowedOperations;
        public IReadOnlySet<string> BlockedOperations => _blockedOperations;
        public IReadOnlySet<ContextScope> AllowedScopes => _allowedScopes;
        public IReadOnlySet<ContextScope> BlockedScopes => _blockedScopes;
        public IReadOnlyDictionary<string, object> ScopeConstraints => _scopeConstraints;
        public static ContextScopeLimiter Create()
        {
            return new ContextScopeLimiter
            {
                _allowAll = true
            };
        }

        public ContextScopeLimiter AllowAll()
        {
            _allowAll = true;
            return this;
        }

        public ContextScopeLimiter DenyAll()
        {
            _allowAll = false;
            return this;
        }
        public static ContextScopeLimiter ForScope(ContextScope scope)
        {
            var limiter = new ContextScopeLimiter();

            switch (scope)
            {
                case ContextScope.Operation:
                    limiter.AllowDataKeys("OperationId", "Input", "Output", "Status");
                    limiter.AllowOperations("Execute", "Validate", "Compile");
                    limiter.AllowScopes(ContextScope.Operation);
                    break;

                case ContextScope.Service:
                    limiter.AllowDataKeys("ServiceId", "ServiceName", "Version", "Configuration");
                    limiter.AllowOperations("Start", "Stop", "Configure", "HealthCheck");
                    limiter.AllowScopes(ContextScope.Operation, ContextScope.Service);
                    break;

                case ContextScope.Domain:
                    limiter.AllowDataKeys("DomainId", "DomainName", "Policies", "Rules");
                    limiter.AllowOperations("Manage", "Configure", "Monitor", "Govern");
                    limiter.AllowScopes(ContextScope.Operation, ContextScope.Service, ContextScope.Domain);
                    break;

                case ContextScope.System:
                    limiter.AllowDataKeys("SystemId", "SystemName", "GlobalConfig", "Infrastructure");
                    limiter.AllowOperations("Administer", "Monitor", "Configure", "Maintain");
                    limiter.AllowScopes(ContextScope.Operation, ContextScope.Service, ContextScope.Domain, ContextScope.System);
                    break;

                case ContextScope.External:
                    limiter.AllowDataKeys("ExternalId", "ExternalName", "Connection", "Protocol");
                    limiter.AllowOperations("Connect", "Exchange", "Synchronize", "Validate");
                    limiter.AllowScopes(ContextScope.Operation, ContextScope.External);
                    break;
            }

            return limiter;
        }
        public ContextScopeLimiter AllowDataKeys(params string[] keys)
        {
            foreach (var key in keys)
            {
                _allowedDataKeys.Add(key);
            }
            return this;
        }
        public ContextScopeLimiter BlockDataKeys(params string[] keys)
        {
            foreach (var key in keys)
            {
                _blockedDataKeys.Add(key);
            }
            return this;
        }
        public ContextScopeLimiter AllowOperations(params string[] operations)
        {
            foreach (var operation in operations)
            {
                _allowedOperations.Add(operation);
            }
            return this;
        }
        public ContextScopeLimiter BlockOperations(params string[] operations)
        {
            foreach (var operation in operations)
            {
                _blockedOperations.Add(operation);
            }
            return this;
        }
        public ContextScopeLimiter AllowScopes(params ContextScope[] scopes)
        {
            foreach (var scope in scopes)
            {
                _allowedScopes.Add(scope);
            }
            return this;
        }
        public ContextScopeLimiter BlockScopes(params ContextScope[] scopes)
        {
            foreach (var scope in scopes)
            {
                _blockedScopes.Add(scope);
            }
            return this;
        }
        public ContextScopeLimiter AddConstraint(string name, object value)
        {
            _scopeConstraints[name] = value;
            return this;
        }
        public bool IsDataKeyAllowed(string key)
        {
            if (_blockedDataKeys.Contains(key))
            {
                return false;
            }

            if (_allowedDataKeys.Count > 0)
            {
                return _allowedDataKeys.Contains(key);
            }

            return _allowAll;
        }
        public bool IsOperationAllowed(string operation)
        {
            if (_blockedOperations.Contains(operation))
            {
                return false;
            }

            if (_allowedOperations.Count > 0)
            {
                return _allowedOperations.Contains(operation);
            }

            return _allowAll;
        }
        public bool IsScopeAllowed(ContextScope scope)
        {
            if (_blockedScopes.Contains(scope))
            {
                return false;
            }

            if (_allowedScopes.Count > 0)
            {
                return _allowedScopes.Contains(scope);
            }

            return _allowAll;
        }
        public T? GetConstraint<T>(string name)
        {
            if (_scopeConstraints.TryGetValue(name, out var value) && value is T typed)
            {
                return typed;
            }
            return default;
        }
        public bool HasConstraint(string name)
        {
            return _scopeConstraints.ContainsKey(name);
        }
        public ContextScopeLimiter Clone()
        {
            var clone = new ContextScopeLimiter();
            clone._allowAll = _allowAll;

            foreach (var key in _allowedDataKeys)
            {
                clone._allowedDataKeys.Add(key);
            }

            foreach (var key in _blockedDataKeys)
            {
                clone._blockedDataKeys.Add(key);
            }

            foreach (var operation in _allowedOperations)
            {
                clone._allowedOperations.Add(operation);
            }

            foreach (var operation in _blockedOperations)
            {
                clone._blockedOperations.Add(operation);
            }

            foreach (var scope in _allowedScopes)
            {
                clone._allowedScopes.Add(scope);
            }

            foreach (var scope in _blockedScopes)
            {
                clone._blockedScopes.Add(scope);
            }

            foreach (var kvp in _scopeConstraints)
            {
                clone._scopeConstraints[kvp.Key] = kvp.Value;
            }

            return clone;
        }
    }
}
