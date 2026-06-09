using System.Collections.Concurrent;

namespace Atelier.Framework.Context.Extensions
{
    public class ContextExtensionRegistry : IContextExtensionRegistry
    {
        private readonly ConcurrentDictionary<Type, IContextExtension> _extensions = new();

        public void Register<TExtension>(TExtension extension) where TExtension : class, IContextExtension
        {
            ArgumentNullException.ThrowIfNull(extension);

            _extensions[extension.GetType()] = extension;
        }

        public void RegisterClone(IContextExtension extension)
        {
            ArgumentNullException.ThrowIfNull(extension);

            _extensions[extension.GetType()] = extension;
        }

        public TExtension? Get<TExtension>() where TExtension : class, IContextExtension
        {
            if (_extensions.TryGetValue(typeof(TExtension), out var exact))
            {
                return exact as TExtension;
            }

            foreach (var candidate in _extensions.Values)
            {
                if (candidate is TExtension match)
                {
                    return match;
                }
            }

            return null;
        }

        public bool TryGet<TExtension>(out TExtension? extension) where TExtension : class, IContextExtension
        {
            extension = Get<TExtension>();
            return extension != null;
        }

        public bool Has<TExtension>() where TExtension : class, IContextExtension
        {
            return Get<TExtension>() != null;
        }

        public void Remove<TExtension>() where TExtension : class, IContextExtension
        {
            if (_extensions.TryRemove(typeof(TExtension), out _))
            {
                return;
            }

            foreach (var kvp in _extensions)
            {
                if (kvp.Value is TExtension)
                {
                    _extensions.TryRemove(kvp.Key, out _);
                    return;
                }
            }
        }

        public IEnumerable<IContextExtension> GetAll()
        {
            return _extensions.Values.ToList();
        }

        public ContextExtensionRegistry Clone()
        {
            var clone = new ContextExtensionRegistry();

            foreach (var kvp in _extensions)
            {
                clone._extensions[kvp.Key] = kvp.Value.Clone();
            }

            return clone;
        }

        public ContextExtensionRegistry CloneWithPropagation()
        {
            var clone = new ContextExtensionRegistry();

            foreach (var kvp in _extensions.Where(e => e.Value.ShouldPropagateToChildren))
            {
                clone._extensions[kvp.Key] = kvp.Value.Clone();
            }

            return clone;
        }
    }
}
