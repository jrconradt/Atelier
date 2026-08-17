using System.Collections.Concurrent;
using Atelier.Framework.Context;
namespace Atelier.Framework.Context
{
    public class ContextManager : IDisposable
    {
        private readonly ConcurrentDictionary<string, IContext> _activeContexts = new();
        public Task<IContext> CreateContextAsync(
            string name,
            ContextScope scope,
            string? serviceId = null,
            string? domainId = null,
            IContext? parent = null,
            CancellationToken cancellationToken = default)
        {
            var contextId = parent != null
                ? $"{parent.ContextId}.{Guid.NewGuid():N}"
                : Guid.NewGuid().ToString();

            var context = new Context(contextId, name, parent)
            {
                Scope = scope,
                ServiceId = serviceId,
                DomainId = domainId,
                CorrelationId = parent?.CorrelationId ?? Guid.NewGuid().ToString(),
                Lifecycle = ContextLifecycle.Creating,
                Status = ContextStatus.Ready
            };

            _activeContexts[contextId] = context;

            return Task.FromResult<IContext>(context);
        }
        public IContext CreateCrossServiceContext(
            IContext parent,
            string targetServiceId,
            string targetDomainId,
            string operationName)
        {
            var childContext = parent.CreateChild(operationName, ContextScope.Service);
            childContext.ServiceId = targetServiceId;
            childContext.DomainId = targetDomainId;
            childContext.CorrelationId = parent.CorrelationId ?? Guid.NewGuid().ToString();

            foreach (var kvp in parent.ServiceMetadata)
            {
                if (kvp.Key.StartsWith("cross-offering:"))
                {
                    childContext.SetServiceMetadata(
                        kvp.Key,
                        kvp.Value);
                }
            }

            childContext.SetServiceMetadata(
                "cross-offering:source-offering",
                parent.ServiceId ?? "unknown");
            childContext.SetServiceMetadata(
                "cross-offering:target-offering",
                targetServiceId);
            childContext.SetServiceMetadata(
                "cross-offering:operation",
                operationName);

            return childContext;
        }
        public IContext EvolveToRuntime(IContext context)
        {
            context.EvolveToRuntime();
            context.UpdateLifecycle(ContextLifecycle.Active);
            return context;
        }
        public Task FinalizeContextAsync(
            IContext context,
            ContextStatus finalStatus,
            CancellationToken cancellationToken = default)
        {
            var postOrder = new List<IContext>();
            var pending = new Stack<IContext>();
            pending.Push(context);

            while (pending.Count > 0)
            {
                var node = pending.Pop();
                node.UpdateStatus(finalStatus);
                node.UpdateLifecycle(ContextLifecycle.Finalizing);
                postOrder.Add(node);

                foreach (var child in node.Children)
                {
                    pending.Push(child);
                }
            }

            for (var i = postOrder.Count - 1; i >= 0; i--)
            {
                var node = postOrder[i];
                node.UpdateLifecycle(ContextLifecycle.Completed);
                _activeContexts.TryRemove(node.ContextId, out _);
            }

            return Task.CompletedTask;
        }
        public Task<IEnumerable<IContext>> GetActiveContextsForServiceAsync(
            string serviceId,
            CancellationToken cancellationToken = default)
        {
            IEnumerable<IContext> result = _activeContexts.Values
                .Where(c => c.ServiceId == serviceId && c.Lifecycle == ContextLifecycle.Active)
                .ToList();

            return Task.FromResult(result);
        }
        public Task<IEnumerable<IContext>> GetActiveContextsForDomainAsync(
            string domainId,
            CancellationToken cancellationToken = default)
        {
            IEnumerable<IContext> result = _activeContexts.Values
                .Where(c => c.DomainId == domainId && c.Lifecycle == ContextLifecycle.Active)
                .ToList();

            return Task.FromResult(result);
        }
        public Task<IEnumerable<IContext>> GetContextHierarchyAsync(
            string rootContextId,
            CancellationToken cancellationToken = default)
        {
            if (_activeContexts.TryGetValue(rootContextId, out var root))
            {
                var descendants = new List<IContext>();
                var pending = new Stack<IContext>();

                foreach (var child in root.Children)
                {
                    pending.Push(child);
                }

                while (pending.Count > 0)
                {
                    var node = pending.Pop();
                    descendants.Add(node);

                    foreach (var child in node.Children)
                    {
                        pending.Push(child);
                    }
                }

                return Task.FromResult<IEnumerable<IContext>>(descendants);
            }

            return Task.FromResult(Enumerable.Empty<IContext>());
        }
        public async Task<int> EvictExpiredContextsAsync(CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;

            var expiredRoots = _activeContexts.Values
                .Where(c => c.Parent == null)
                .Where(c => c.ExpiresAt.HasValue && c.ExpiresAt.Value <= now)
                .ToList();

            var evicted = 0;
            foreach (var root in expiredRoots)
            {
                await FinalizeContextAsync(
                    root,
                    ContextStatus.Cancelled,
                    cancellationToken).ConfigureAwait(false);
                evicted++;
            }

            return evicted;
        }
        public bool ValidateContextRequirements(IContext context, IEnumerable<string> requiredKeys)
        {
            if (!context.IsCompileTime)
            {
                return false;
            }

            foreach (var key in requiredKeys)
            {
                if (!context.TryGetValue(key, out _))
                {
                    return false;
                }
            }

            return true;
        }
        public void AddCompileTimeType(IContext context, string key, Type type)
        {
            context.AddCompileTimeType(
                key,
                type);
        }
        public Type? GetCompileTimeType(IContext context, string key)
        {
            return context.CompileTimeTypes.TryGetValue(key, out var type) ? type : null;
        }

        public void Dispose()
        {
            var pending = new Stack<IContext>();
            foreach (var root in _activeContexts.Values.Where(c => c.Parent == null))
            {
                pending.Push(root);
            }

            while (pending.Count > 0)
            {
                var node = pending.Pop();
                node.UpdateStatus(ContextStatus.Cancelled);
                node.UpdateLifecycle(ContextLifecycle.Completed);

                foreach (var child in node.Children)
                {
                    pending.Push(child);
                }
            }

            _activeContexts.Clear();
        }
    }
}
