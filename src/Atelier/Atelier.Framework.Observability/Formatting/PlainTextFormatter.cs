using Atelier.Framework.Observability;

namespace Atelier.Framework.Observability.Formatting
{
    public sealed class PlainTextFormatter : ILogFormatter
    {
        public string Format(LoggingContext loggingContext)
        {
            var contextName = Sanitize(loggingContext.Context.GetType().Name);
            var lines = new List<string>
            {
                $"[{contextName}] [ContextId:{loggingContext.Context.ContextId}] {Sanitize(loggingContext.Message)}"
            };

            if (loggingContext.Exception != null)
            {
                lines.Add($"[{Sanitize(loggingContext.Exception.GetType().Name)}] {Sanitize(SensitiveValueRedactor.RedactText(loggingContext.Exception.Message))}");
            }

            foreach (var value in loggingContext.Values)
            {
                lines.Add($"[{Sanitize(value.Key)}] {Sanitize(ValueFormatter.FormatValue(value.Value))}");
            }

            return string.Join(Environment.NewLine, lines);
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
