using Atelier.Framework.Observability;

namespace Atelier.Framework.Observability.Formatting
{
    public sealed class CompactFormatter : ILogFormatter
    {
        public string Format(LoggingContext loggingContext)
        {
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var contextName = Sanitize(loggingContext.Context.Name);
            var entry = $"[{timestamp}] [{contextName}] [ContextId:{loggingContext.Context.ContextId}] {Sanitize(loggingContext.Message)}";

            if (loggingContext.Exception != null)
            {
                entry += $" | Exception: {Sanitize(loggingContext.Exception.GetType().Name)} - {Sanitize(SensitiveValueRedactor.RedactText(loggingContext.Exception.Message))}";
            }

            if (loggingContext.Values.Any())
            {
                var values = string.Join(
                    ", ",
                    loggingContext.Values.Select(kvp => $"{Sanitize(kvp.Key)}={Sanitize(ValueFormatter.FormatValue(kvp.Value))}"));
                entry += $" | {values}";
            }

            return entry;
        }

        private static string Sanitize(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var result = new List<char>(value.Length);
            foreach (var ch in value)
            {
                if (char.IsControl(ch))
                {
                    result.Add(' ');
                }
                else
                {
                    result.Add(ch);
                }
            }

            return new string(result.ToArray());
        }
    }
}
