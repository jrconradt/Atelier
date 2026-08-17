using Atelier.Framework.Context;
using Atelier.Framework.Observability;
using Atelier.Framework.Observability.Strategy;
namespace Atelier.Framework.Observability
{
    public sealed class Logger : ILogger
    {
        private sealed record State
        {
            public IReadOnlyDictionary<string, object> Values { get; init; } = EmptyValues;
            public string Message { get; init; } = string.Empty;
            public Exception? Exception { get; init; }
            public ILoggingStrategy LoggingStrategy { get; init; } = null!;
            public LogLevel Level { get; init; } = LogLevel.Information;
            public bool IncludeContextMetadata { get; init; }
            public bool IncludeHierarchyMetadata { get; init; }
            public bool IncludeAuthorizationSummary { get; init; }
            public bool IncludeScopeLimiterSummary { get; init; }
            public bool IncludeFilteredData { get; init; }
        }

        private static readonly IReadOnlyDictionary<string, object> EmptyValues = new Dictionary<string, object>();

        private readonly State _state;
        private int _minimumLevel = (int)LogLevel.Information;

        public Logger(
            ILoggingStrategy loggingStrategy)
        {
            ArgumentNullException.ThrowIfNull(loggingStrategy);
            _state = new State
            {
                LoggingStrategy = loggingStrategy,
            };
        }

        private Logger(
            State state,
            int minimumLevel)
        {
            _state = state;
            _minimumLevel = minimumLevel;
        }

        private Logger With(State state)
        {
            return new Logger(state, Volatile.Read(ref _minimumLevel));
        }

        private static Dictionary<string, object> CopyValues(IReadOnlyDictionary<string, object> values)
        {
            return new Dictionary<string, object>(values);
        }

        public ILogger WithValues(params ReadOnlySpan<(string Key, object Value)> values)
        {
            var merged = CopyValues(_state.Values);
            foreach (var pair in values)
            {
                merged[pair.Key] = pair.Value;
            }
            return With(_state with { Values = merged });
        }

        public ILogger WithValue(
            string key,
            object value)
        {
            var merged = CopyValues(_state.Values);
            merged[key] = value;
            return With(_state with { Values = merged });
        }

        public ILogger WithError(Exception exception)
        {
            return With(_state with { Exception = exception });
        }

        public LogLevel MinimumLevel
        {
            get => (LogLevel)Volatile.Read(ref _minimumLevel);
            set => Volatile.Write(ref _minimumLevel, (int)value);
        }

        public bool IsEnabled(LogLevel level)
        {
            return level >= MinimumLevel;
        }

        public async Task LogAsync(CancellationToken cancellationToken = default)
        {
            if (_state.LoggingStrategy == null
                || _state.Level < MinimumLevel)
            {
                return;
            }

            var currentContext = AmbientContext.Current;

            var values = CopyValues(_state.Values);

            if (!string.IsNullOrEmpty(currentContext.TraceId))
            {
                values["TraceId"] = currentContext.TraceId;
            }

            if (!string.IsNullOrEmpty(currentContext.SpanId))
            {
                values["SpanId"] = currentContext.SpanId;
            }

            if (!string.IsNullOrEmpty(currentContext.ParentSpanId))
            {
                values["ParentSpanId"] = currentContext.ParentSpanId;
            }

            if (!string.IsNullOrEmpty(currentContext.CorrelationId))
            {
                values["CorrelationId"] = currentContext.CorrelationId;
            }

            var needsMetadata = _state.IncludeContextMetadata
                || _state.IncludeHierarchyMetadata
                || _state.IncludeAuthorizationSummary
                || _state.IncludeScopeLimiterSummary;

            if (needsMetadata)
            {
                var metadataContext = new LoggingContext(
                    currentContext,
                    string.Empty,
                    null,
                    new Dictionary<string, object>(),
                    _state.Level);

                if (_state.IncludeContextMetadata)
                {
                    foreach (var kvp in metadataContext.GetContextMetadata())
                    {
                        values[kvp.Key] = kvp.Value;
                    }
                }

                if (_state.IncludeHierarchyMetadata)
                {
                    foreach (var kvp in metadataContext.GetHierarchyMetadata())
                    {
                        values[kvp.Key] = kvp.Value;
                    }
                }

                if (_state.IncludeAuthorizationSummary)
                {
                    foreach (var kvp in metadataContext.GetAuthorizationSummary())
                    {
                        values[kvp.Key] = kvp.Value;
                    }
                }

                if (_state.IncludeScopeLimiterSummary)
                {
                    foreach (var kvp in metadataContext.GetScopeLimiterSummary())
                    {
                        values[kvp.Key] = kvp.Value;
                    }
                }
            }

            if (_state.IncludeFilteredData)
            {
                foreach (var kvp in currentContext.GetFilteredData())
                {
                    values[$"FilteredData.{kvp.Key}"] = kvp.Value;
                }
            }

            var enrichedContext = new LoggingContext(
                currentContext,
                _state.Message,
                _state.Exception,
                values,
                _state.Level);

            await _state.LoggingStrategy.TraverseAsync(enrichedContext, cancellationToken).ConfigureAwait(false);
        }

        public void Log()
        {
            var task = LogAsync(CancellationToken.None);
            if (!task.IsCompleted)
            {
                task.ContinueWith(
                    static t => ReportLoggingFailure(t.Exception),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            else if (task.IsFaulted)
            {
                ReportLoggingFailure(task.Exception);
            }
        }

        private static void ReportLoggingFailure(AggregateException? exception)
        {
            if (exception is null)
            {
                return;
            }

            try
            {
                var reason = SensitiveValueRedactor.RedactText(exception.GetBaseException().Message);
                Console.Error.WriteLine($"[Atelier.Observability] logging pipeline failure: {reason}");
            }
            catch
            {
            }
        }

        public ILogger WithMessage(string message)
        {
            return With(_state with { Message = message });
        }

        public ILogger WithMessage(
            string message,
            params object[] args)
        {
            return With(_state with { Message = string.Format(message, args) });
        }

        public ILogger WithMessage(Exception exception)
        {
            return With(_state with { Exception = exception, Message = exception.Message });
        }

        public ILogger WithLoggingStrategy(ILoggingStrategy strategy)
        {
            ArgumentNullException.ThrowIfNull(strategy);
            return With(_state with { LoggingStrategy = strategy });
        }

        public ILogger WithLevel(LogLevel level)
        {
            return With(_state with { Level = level });
        }

        public ILogger WithContextMetadata()
        {
            return With(_state with { IncludeContextMetadata = true });
        }

        public ILogger WithHierarchyMetadata()
        {
            return With(_state with { IncludeHierarchyMetadata = true });
        }

        public ILogger WithAuthorizationSummary()
        {
            return With(_state with { IncludeAuthorizationSummary = true });
        }

        public ILogger WithScopeLimiterSummary()
        {
            return With(_state with { IncludeScopeLimiterSummary = true });
        }

        public ILogger WithFilteredData()
        {
            return With(_state with { IncludeFilteredData = true });
        }
    }
}
