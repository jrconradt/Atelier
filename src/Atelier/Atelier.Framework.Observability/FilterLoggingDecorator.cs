using Atelier.Framework.Observability.Strategy;

namespace Atelier.Framework.Observability
{
    public sealed class FilterLoggingDecorator : LoggingStrategyDecorator
    {
        private readonly Func<LoggingContext, bool> _filter;

        public FilterLoggingDecorator(
            ILoggingStrategy innerStrategy,
            Func<LoggingContext, bool> filter)
            : base(innerStrategy)
        {
            ArgumentNullException.ThrowIfNull(filter);
            _filter = filter;
        }

        public override Task TraverseAsync(
            LoggingContext loggingContext,
            CancellationToken cancellationToken = default)
        {
            if (_filter(loggingContext))
            {
                return _innerStrategy.TraverseAsync(loggingContext, cancellationToken);
            }
            return Task.CompletedTask;
        }
    }

    public static class FilterLoggingDecoratorExtensions
    {
        public static ILoggingStrategy OnlyErrors(this ILoggingStrategy strategy)
        {
            return new FilterLoggingDecorator(
                strategy,
                ctx => ctx.Exception != null);
        }

        public static ILoggingStrategy OnlyMessages(
            this ILoggingStrategy strategy,
            Func<string, bool> messagePredicate)
        {
            return new FilterLoggingDecorator(
                strategy,
                ctx => messagePredicate(ctx.Message));
        }

        public static ILoggingStrategy ExcludeContexts(
            this ILoggingStrategy strategy,
            params string[] contextNames)
        {
            var excludedNames = new HashSet<string>(contextNames);
            return new FilterLoggingDecorator(
                strategy,
                ctx => !excludedNames.Contains(ctx.Context.Name));
        }
    }
}





