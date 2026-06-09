using Atelier.Framework.Observability.Formatting;

namespace Atelier.Framework.Observability.Strategy;

public sealed class ConsoleLoggingStrategy : ILoggingStrategy
{
    private readonly ILogFormatter _formatter;

    public ConsoleLoggingStrategy(ILogFormatter? formatter = null)
    {
        _formatter = formatter ?? new PlainTextFormatter();
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
