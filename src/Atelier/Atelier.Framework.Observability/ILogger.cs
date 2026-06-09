using Atelier.Framework.Context;
using Atelier.Framework.Observability.Strategy;

namespace Atelier.Framework.Observability;

public interface ILogger
{
    public ILogger WithLoggingStrategy(ILoggingStrategy strategy);
    public ILogger WithValue(string key, object value);
    public ILogger WithValues(params ReadOnlySpan<(string Key, object Value)> values);
    public ILogger WithError(Exception exception);
    public ILogger WithMessage(string message);
    public ILogger WithMessage(string message, params object[] args);
    public ILogger WithMessage(Exception exception);
    public ILogger WithLevel(LogLevel level);

    public LogLevel MinimumLevel { get; set; }

    public bool IsEnabled(LogLevel level);

    public ILogger WithContextMetadata();
    public ILogger WithHierarchyMetadata();
    public ILogger WithAuthorizationSummary();
    public ILogger WithScopeLimiterSummary();
    public ILogger WithFilteredData();

        public Task LogAsync(CancellationToken cancellationToken = default);

        public void Log();
}



