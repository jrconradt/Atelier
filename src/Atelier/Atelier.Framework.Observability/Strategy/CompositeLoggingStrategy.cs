namespace Atelier.Framework.Observability.Strategy
{
    public sealed class CompositeLoggingStrategy : ILoggingStrategy
    {
        private readonly List<ILoggingStrategy> _strategies;

        public CompositeLoggingStrategy(params ILoggingStrategy[] strategies)
        {
            _strategies = new List<ILoggingStrategy>(strategies ?? Array.Empty<ILoggingStrategy>());
        }

        public void AddStrategy(ILoggingStrategy strategy)
        {
            if (strategy != null)
            {
                _strategies.Add(strategy);
            }
        }

        public void RemoveStrategy(ILoggingStrategy strategy)
        {
            _strategies.Remove(strategy);
        }

        public async Task TraverseAsync(
            LoggingContext loggingContext,
            CancellationToken cancellationToken = default)
        {
            foreach (var strategy in _strategies)
            {
                await strategy.TraverseAsync(loggingContext, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
