using Atelier.Framework.Outcomes;

namespace Atelier.Framework.Context.Extensions
{
    public class OutcomeAggregationExtension : IContextExtension
    {
        private readonly Dictionary<string, object> _outcomes = new();

        public string ExtensionName => "OutcomeAggregation";

        public bool ShouldPropagateToChildren => false;

        public IReadOnlyDictionary<string, object> Outcomes => _outcomes;

        public void AddOutcome<T>(string key, Outcome<T> outcome)
        {
            _outcomes[key] = outcome;
        }

        public bool TryGetOutcome<T>(string key, out Outcome<T> outcome)
        {
            if (_outcomes.TryGetValue(key, out var value) && value is Outcome<T> typed)
            {
                outcome = typed;
                return true;
            }
            outcome = default;
            return false;
        }

        public bool HasOutcome(string key) => _outcomes.ContainsKey(key);

        public IContextExtension Clone()
        {
            var clone = new OutcomeAggregationExtension();
            foreach (var kvp in _outcomes)
            {
                clone._outcomes[kvp.Key] = kvp.Value;
            }
            return clone;
        }
    }
}
