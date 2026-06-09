using Atelier.Framework.Context;
namespace Atelier.Framework.Context
{
    public class CompositeContext : Context
    {
        private readonly Dictionary<string, string> _additionalData;

        public CompositeContext(
            IContext? parent,
            Dictionary<string, string>? additionalData = null)
            : base(
                parent != null ? $"{parent.ContextId}.{Guid.NewGuid():N}" : Guid.NewGuid().ToString(),
                parent != null ? $"Composite-{parent.Name}" : "CompositeContext",
                parent,
                new Dictionary<string, string>(),
                additionalData ?? new Dictionary<string, string>())
        {
            _additionalData = additionalData ?? new Dictionary<string, string>();
        }

        public CompositeContext(
            string contextId,
            string name,
            IContext? parent = null,
            Dictionary<string, string>? additionalData = null)
            : base(
                contextId,
                name,
                parent,
                new Dictionary<string, string>(),
                additionalData ?? new Dictionary<string, string>())
        {
            _additionalData = additionalData ?? new Dictionary<string, string>();
        }

        public override bool TryGetValue(string key, out string value)
        {
            IContext? current = this;
            while (current is CompositeContext composite)
            {
                if (composite._additionalData.TryGetValue(key, out value!))
                {
                    return true;
                }

                current = composite.Parent;
            }

            if (current != null)
            {
                return current.TryGetValue(key, out value!);
            }

            value = null!;
            return false;
        }

        public override IReadOnlyDictionary<string, string> GetAllValues()
        {
            var chain = new Stack<IReadOnlyDictionary<string, string>>();
            IContext? current = this;

            while (current is CompositeContext composite)
            {
                chain.Push(composite._additionalData);
                current = composite.Parent;
            }

            var merged = new Dictionary<string, string>();

            if (current != null)
            {
                foreach (var kvp in current.GetAllValues())
                {
                    merged[kvp.Key] = kvp.Value;
                }
            }

            while (chain.Count > 0)
            {
                foreach (var kvp in chain.Pop())
                {
                    merged[kvp.Key] = kvp.Value;
                }
            }

            return merged;
        }

        public override void AddValue(string key, string value)
        {
            if (!IsDataKeyAllowed(key))
            {
                throw new InvalidOperationException($"Data key '{key}' is not allowed in context scope '{Scope}'");
            }
            _additionalData[key] = value;
        }

        public IContext WithAdditionalData(Dictionary<string, string> data)
        {
            var newData = new Dictionary<string, string>(_additionalData);
            foreach (var kvp in data)
            {
                newData[kvp.Key] = kvp.Value;
            }

            return new CompositeContext(
                ContextId,
                Name,
                Parent,
                newData);
        }

        public static IContext FromParent(
            IContext parent,
            params (string key, string value)[] additionalValues)
        {
            var data = new Dictionary<string, string>();
            foreach (var (key, value) in additionalValues)
            {
                data[key] = value;
            }

            return new CompositeContext(
                parent,
                data);
        }
    }
}
