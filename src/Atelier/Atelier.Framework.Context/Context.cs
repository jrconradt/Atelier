using Atelier.Framework.Context;
using Atelier.Framework.Context.Extensions;
using Atelier.Framework.Outcomes;
namespace Atelier.Framework.Context
{
    public abstract class Context : IContext
    {
        private string _contextId;
        private string _name;
        private IContext? _parent;
        private readonly Dictionary<string, string> _data;
        private readonly Dictionary<string, string> _additionalData;
        private readonly List<IContext> _children = new();
        private readonly Dictionary<string, object> _results = new();
        private readonly Dictionary<string, Type> _compileTimeTypes = new();
        private readonly Dictionary<string, string> _serviceMetadata = new();
        private readonly IContextExtensionRegistry _extensions = new ContextExtensionRegistry();

        public static Context Empty => new EmptyContext();

        public string ContextId
        {
            get => _contextId;
            set => _contextId = value;
        }

        public string Name
        {
            get => _name;
            set => _name = value;
        }

        public IContext? Parent
        {
            get => _parent;
            set => _parent = value;
        }

        public ContextScope Scope { get; set; } = ContextScope.Operation;
        public ContextLifecycle Lifecycle { get; set; } = ContextLifecycle.Creating;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ExpiresAt { get; set; }

        public string? ServiceId { get; set; }
        public string? DomainId { get; set; }
        public string? CorrelationId { get; set; }
        public string? TraceId { get; set; }
        public string? SpanId { get; set; }
        public string? ParentSpanId { get; set; }
        public IReadOnlyDictionary<string, string> ServiceMetadata => _serviceMetadata;

        public IContextExtensionRegistry Extensions => _extensions;

        public IReadOnlyList<IContext> Children => _children;
        public ContextStatus Status { get; set; } = ContextStatus.Ready;
        public IReadOnlyDictionary<string, object> Results => _results;

        public bool IsCompileTime { get; set; } = true;
        public bool IsRuntime => !IsCompileTime;
        public IReadOnlyDictionary<string, Type> CompileTimeTypes => _compileTimeTypes;

        public AuthorizationContext? Authorization { get; set; }

        public ContextScopeLimiter? ScopeLimiter { get; set; }

        public IReadOnlyDictionary<string, string> Data => _data;

        public IReadOnlyDictionary<string, string> AdditionalData => _additionalData;

        public Context(
            string contextId,
            string name,
            IContext? parent,
            Dictionary<string, string> data,
            Dictionary<string, string> additionalData)
        {
            _contextId = contextId;
            _name = name;
            _parent = parent;
            _data = data;
            _additionalData = additionalData;
            ScopeLimiter = ContextScopeLimiter.Create();

            CorrelationId = parent?.CorrelationId ?? Guid.NewGuid().ToString();

            RegisterWithParent();
        }

        private void RegisterWithParent()
        {
            if (_parent is Context parentContext)
            {
                parentContext._children.Add(this);
            }
        }

        public virtual bool TryGetValue(string key, out string value)
        {
            return _data.TryGetValue(key, out value!);
        }

        public virtual IReadOnlyDictionary<string, string> GetAllValues()
        {
            return _data;
        }

        public virtual void AddValue(string key, string value)
        {
            if (!IsDataKeyAllowed(key))
            {
                throw new InvalidOperationException($"Data key '{key}' is not allowed in context scope '{Scope}'");
            }
            _data[key] = value;
        }

        public virtual IContext CreateChild(string name, ContextScope scope)
        {
            if (!IsScopeAllowed(scope))
            {
                throw new InvalidOperationException($"Context scope '{scope}' is not allowed as child of '{Scope}'");
            }

            var childId = $"{ContextId}.{Guid.NewGuid():N}";
            var child = new CompositeContext(childId, name, this);
            child.Scope = scope;

            PropagateInheritableState(child);

            return child;
        }

        public void PropagateInheritableState(Context child)
        {
            child.ServiceId = ServiceId;
            child.DomainId = DomainId;
            child.CorrelationId = CorrelationId ?? Guid.NewGuid().ToString();
            child.TraceId = TraceId;
            child.SpanId = SpanId;
            child.ParentSpanId = ParentSpanId;

            foreach (var kvp in _serviceMetadata)
            {
                child.SetServiceMetadata(kvp.Key, kvp.Value);
            }

            PropagateSecurityState(child);
        }

        private void PropagateSecurityState(Context child)
        {
            if (child._extensions is ContextExtensionRegistry childRegistry)
            {
                foreach (var extension in _extensions.GetAll())
                {
                    if (extension.ShouldPropagateToChildren)
                    {
                        childRegistry.RegisterClone(extension.Clone());
                    }
                }
            }

            if (ScopeLimiter != null)
            {
                child.ScopeLimiter = ScopeLimiter.Clone();
            }

            if (Authorization != null)
            {
                child.Authorization = AuthorizationContext.InheritFrom(Authorization);
            }
        }

        public void SetServiceMetadata(string key, string value)
        {
            _serviceMetadata[key] = value;
        }

        public virtual void AddResult(string key, object? result)
        {
            if (result != null && !IsPrimitiveOrAllowedType(result.GetType()))
            {
                throw new InvalidOperationException(
                    $"AddResult only accepts primitive types (string, int, DateTime, etc.). " +
                    $"For entities, use Context.AddEntityRef(). " +
                    $"For complex objects, store them in appropriate repositories and pass references via context. " +
                    $"Attempted to store: {result.GetType().FullName}");
            }
            _results[key] = result!;
        }

        private static bool IsPrimitiveOrAllowedType(Type type)
        {
            var current = type;
            while (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                current = Nullable.GetUnderlyingType(current)!;
            }

            return current.IsPrimitive
                || current == typeof(string)
                || current == typeof(DateTime)
                || current == typeof(DateTimeOffset)
                || current == typeof(TimeSpan)
                || current == typeof(decimal)
                || current == typeof(Guid)
                || current.IsEnum
                || (current.IsArray && current.GetElementType()?.IsPrimitive == true);
        }

        public virtual T? GetOutcome<T>(string key)
        {
            if (_results.TryGetValue(key, out var value) && value is T typed)
            {
                return typed;
            }
            return default;
        }

        public virtual bool HasResult(string key)
        {
            return _results.ContainsKey(key);
        }

        public virtual IContext EvolveToRuntime()
        {
            IsCompileTime = false;
            Lifecycle = ContextLifecycle.Active;
            Status = ContextStatus.Ready;
            return this;
        }

        public virtual void UpdateStatus(ContextStatus status)
        {
            Status = status;
        }

        public virtual void UpdateLifecycle(ContextLifecycle lifecycle)
        {
            Lifecycle = lifecycle;
        }

        public virtual void AddCompileTimeType(string key, Type type)
        {
            if (IsCompileTime)
            {
                _compileTimeTypes[key] = type;
            }
        }

        public virtual IContext WithAuthorization(AuthorizationContext authorization)
        {
            Authorization = authorization;
            return this;
        }

        public virtual IContext InheritAuthorization()
        {
            if (Parent?.Authorization != null)
            {
                Authorization = AuthorizationContext.InheritFrom(Parent.Authorization);
            }
            return this;
        }

        public virtual bool HasAuthorization()
        {
            return Authorization != null
                && Authorization.IsVerified
                && Authorization.IsValid();
        }

        public virtual bool IsAuthorized(string permission)
        {
            return Authorization != null
                && Authorization.IsVerified
                && Authorization.IsValid()
                && Authorization.HasPermission(permission);
        }

        public virtual bool IsAuthorizedForRole(string role)
        {
            return Authorization != null
                && Authorization.IsVerified
                && Authorization.IsValid()
                && Authorization.HasRole(role);
        }

        public virtual IContext WithScopeLimiter(ContextScopeLimiter limiter)
        {
            ScopeLimiter = limiter;
            return this;
        }

        public virtual IContext InheritScopeLimiter()
        {
            if (Parent?.ScopeLimiter != null)
            {
                ScopeLimiter = Parent.ScopeLimiter.Clone();
            }
            return this;
        }

        public virtual bool HasScopeLimiter()
        {
            return ScopeLimiter != null;
        }

        public virtual bool IsDataKeyAllowed(string key)
        {
            return ScopeLimiter?.IsDataKeyAllowed(key) ?? false;
        }

        public virtual bool IsOperationAllowed(string operation)
        {
            return ScopeLimiter?.IsOperationAllowed(operation) ?? false;
        }

        public virtual bool IsScopeAllowed(ContextScope scope)
        {
            return ScopeLimiter?.IsScopeAllowed(scope) ?? false;
        }

        public virtual T? GetScopeConstraint<T>(string name)
        {
            if (ScopeLimiter == null)
            {
                return default;
            }

            return ScopeLimiter.GetConstraint<T>(name);
        }

        bool IContext.TryGetValue(string key, out string value)
        {
            return TryGetValue(key, out value);
        }

        IReadOnlyDictionary<string, string> IContext.GetAllValues()
        {
            return GetAllValues();
        }

        public static IContext CreateEmpty =>
            new EmptyContext();

        public static IContext CreateSystemContext(string operationName) =>
            new SystemContext(operationName);

        private class EmptyContext : Context
        {
            public EmptyContext() : base(
                Guid.Empty.ToString(),
                "Empty",
                null,
                new Dictionary<string, string>(),
                new Dictionary<string, string>())
            {
                Scope = ContextScope.System;
                Lifecycle = ContextLifecycle.Completed;
                Status = ContextStatus.Ready;
                IsCompileTime = false;
            }
        }

        private class SystemContext : Context
        {
            public SystemContext(string operationName) : base(
                Guid.NewGuid().ToString(),
                $"System_{operationName}",
                null,
                new Dictionary<string, string>
                {
                    ["OperationType"] = "System",
                    ["OperationName"] = operationName,
                    ["InitiatedBy"] = "BackgroundService"
                },
                new Dictionary<string, string>())
            {
                Scope = ContextScope.System;
                Lifecycle = ContextLifecycle.Active;
                Status = ContextStatus.Ready;
                IsCompileTime = false;
                ServiceId = "atelier-system";
                DomainId = "system";
                CorrelationId = Guid.NewGuid().ToString();
            }
        }
    }
}
