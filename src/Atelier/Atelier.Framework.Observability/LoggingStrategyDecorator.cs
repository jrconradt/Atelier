using Atelier.Framework.Observability.Strategy;

namespace Atelier.Framework.Observability
{
    public abstract class LoggingStrategyDecorator : ILoggingStrategy
    {
        protected readonly ILoggingStrategy _innerStrategy;

        protected LoggingStrategyDecorator(ILoggingStrategy innerStrategy)
        {
            ArgumentNullException.ThrowIfNull(innerStrategy);
            _innerStrategy = innerStrategy;
        }

        public virtual Task TraverseAsync(
            LoggingContext loggingContext,
            CancellationToken cancellationToken = default)
        {
            return _innerStrategy.TraverseAsync(loggingContext, cancellationToken);
        }
    }
}
