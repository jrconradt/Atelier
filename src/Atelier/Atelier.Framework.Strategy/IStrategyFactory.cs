using Atelier.Framework.Outcomes;

namespace Atelier.Framework.Strategy
{
    public interface IStrategyFactory<TStrategy, TKey>
    {
        public Outcome<TStrategy> GetStrategy(TKey key);
        public void RegisterStrategy(
            TKey key,
            TStrategy strategy);
        public bool HasStrategy(TKey key);
    }
}

