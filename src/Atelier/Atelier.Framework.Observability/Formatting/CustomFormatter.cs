using Atelier.Framework.Observability;

namespace Atelier.Framework.Observability.Formatting
{
    public sealed class CustomFormatter : ILogFormatter
    {
        private readonly Func<LoggingContext, string> _formatFunction;

        public CustomFormatter(Func<LoggingContext, string> formatFunction)
        {
            ArgumentNullException.ThrowIfNull(formatFunction);
            _formatFunction = formatFunction;
        }

        public string Format(LoggingContext loggingContext)
        {
            return _formatFunction(loggingContext);
        }
    }
}





