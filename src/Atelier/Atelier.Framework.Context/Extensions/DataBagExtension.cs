namespace Atelier.Framework.Context.Extensions
{
    public class DataBagExtension : IContextExtension
    {
        private readonly Dictionary<string, string> _data = new();

        public string ExtensionName => "DataBag";

        public bool ShouldPropagateToChildren => false;

        public IReadOnlyDictionary<string, string> Data => _data;

        public void Set(string key, string value)
        {
            _data[key] = value;
        }

        public bool TryGet(string key, out string? value)
        {
            return _data.TryGetValue(key, out value);
        }

        public string? Get(string key)
        {
            return _data.TryGetValue(key, out var value) ? value : null;
        }

        public IContextExtension Clone()
        {
            var clone = new DataBagExtension();
            foreach (var kvp in _data)
            {
                clone._data[kvp.Key] = kvp.Value;
            }
            return clone;
        }
    }
}
