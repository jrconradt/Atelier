using Atelier.Framework.Outcomes;

namespace Atelier.Framework.Strategy
{
    public sealed class StrategyFactory<TStrategy, TKey> : IStrategyFactory<TStrategy, TKey>
        where TKey : notnull
    {
        private readonly Dictionary<TKey, TStrategy> _strategies = new();
        private readonly TStrategy? _fallbackStrategy;

        public StrategyFactory(TStrategy? fallbackStrategy)
        {
            _fallbackStrategy = fallbackStrategy;
        }

        public Outcome<TStrategy> GetStrategy(TKey key)
        {
            if (_strategies.TryGetValue(
                key,
                out var strategy))
            {
                return Outcome<TStrategy>.Success(strategy);
            }

            if (_fallbackStrategy != null)
            {
                return Outcome<TStrategy>.Success(_fallbackStrategy);
            }

            return Outcome<TStrategy>.Failure();
        }

        public void RegisterStrategy(
            TKey key,
            TStrategy strategy)
        {
            _strategies[key] = strategy;
        }

        public bool HasStrategy(TKey key)
        {
            return _strategies.ContainsKey(key);
        }
    }
}

