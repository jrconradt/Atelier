namespace Atelier.Framework.Context.Extensions
{
    public class CompilationContextExtension : IContextExtension
    {
        private readonly Dictionary<string, Type> _compileTimeTypes = new();

        public string ExtensionName => "Compilation";

        public bool ShouldPropagateToChildren => true;

        public IReadOnlyDictionary<string, Type> CompileTimeTypes => _compileTimeTypes;

        public void AddType(string key, Type type)
        {
            _compileTimeTypes[key] = type;
        }

        public Type? GetType(string key)
        {
            return _compileTimeTypes.TryGetValue(key, out var type) ? type : null;
        }

        public bool HasType(string key) => _compileTimeTypes.ContainsKey(key);

        public IContextExtension Clone()
        {
            var clone = new CompilationContextExtension();
            foreach (var kvp in _compileTimeTypes)
            {
                clone._compileTimeTypes[kvp.Key] = kvp.Value;
            }
            return clone;
        }
    }
}
