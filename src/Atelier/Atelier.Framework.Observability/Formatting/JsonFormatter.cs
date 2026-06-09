using System.Text.Json;

namespace Atelier.Framework.Observability.Formatting
{
    public sealed class JsonFormatter : ILogFormatter
    {
        private const int MAX_EXCEPTION_MESSAGE_LENGTH = 256;

        private readonly JsonSerializerOptions _options;
        private readonly bool _verboseExceptions;

        public JsonFormatter(
            bool indented = false,
            bool verboseExceptions = false)
        {
            _options = new JsonSerializerOptions
            {
                WriteIndented = indented
            };
            _verboseExceptions = verboseExceptions;
        }

        public string Format(LoggingContext loggingContext)
        {
            var logEntry = new
            {
                Timestamp = DateTime.UtcNow,
                ContextType = loggingContext.Context.GetType().Name,
                loggingContext.Context.ContextId,
                loggingContext.Message,
                Exception = loggingContext.Exception != null ? new
                {
                    Type = loggingContext.Exception.GetType().FullName,
                    Message = RedactExceptionMessage(loggingContext.Exception.Message),
                    StackTrace = _verboseExceptions ? SensitiveValueRedactor.RedactText(loggingContext.Exception.StackTrace) : null
                } : null,
                loggingContext.Values
            };

            return JsonSerializer.Serialize(
                logEntry,
                _options);
        }

        private static string RedactExceptionMessage(string? message)
        {
            var scrubbed = SensitiveValueRedactor.RedactText(message);

            if (scrubbed.Length > MAX_EXCEPTION_MESSAGE_LENGTH)
            {
                return $"{scrubbed.Substring(0, MAX_EXCEPTION_MESSAGE_LENGTH)}…";
            }

            return scrubbed;
        }
    }
}





