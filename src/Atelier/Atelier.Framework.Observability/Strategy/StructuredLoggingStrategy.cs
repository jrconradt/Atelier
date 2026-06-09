using Atelier.Framework.Observability.Formatting;

namespace Atelier.Framework.Observability.Strategy
{
    public class StructuredLoggingStrategy : ILoggingStrategy
    {
        private readonly ILogFormatter _formatter;

        public StructuredLoggingStrategy(ILogFormatter? formatter = null)
        {
            _formatter = formatter ?? new Formatting.JsonFormatter();
        }

        public Task TraverseAsync(
            LoggingContext loggingContext,
            CancellationToken cancellationToken = default)
        {
            var formatted = _formatter.Format(loggingContext);
            Console.WriteLine(formatted);
            return Task.CompletedTask;
        }
    }
}
