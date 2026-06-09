using Atelier.Framework.Primitives;
using System.Collections.Concurrent;
using Atelier.Framework.Attributes;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;
using Microsoft.Extensions.Configuration;
using Polly;
using Prometheus;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace Atelier.Framework.Resilience;

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class ResiliencePipelineFactory : IAtelier
{
    [Requisite] protected readonly IConfiguration _configuration = null!;

    private readonly ConcurrentDictionary<string, ResiliencePipeline> _pipelines = new();

    private static readonly Counter RetriesTotal = Prometheus.Metrics.CreateCounter(
        "atelier_resilience_retries_total",
        "Total number of retry attempts performed by resilience pipelines",
        new CounterConfiguration
        {
            LabelNames = new[] { "pipeline" }
        });

    private static readonly Counter CircuitBreakerOpenedTotal = Prometheus.Metrics.CreateCounter(
        "atelier_resilience_circuit_breaker_opened_total",
        "Total number of times a resilience pipeline circuit breaker opened",
        new CounterConfiguration
        {
            LabelNames = new[] { "pipeline" }
        });

    private static readonly Counter CircuitBreakerHalfOpenedTotal = Prometheus.Metrics.CreateCounter(
        "atelier_resilience_circuit_breaker_half_opened_total",
        "Total number of times a resilience pipeline circuit breaker transitioned to half-open",
        new CounterConfiguration
        {
            LabelNames = new[] { "pipeline" }
        });

    private static readonly Gauge CircuitBreakerState = Prometheus.Metrics.CreateGauge(
        "atelier_resilience_circuit_breaker_state",
        "Resilience pipeline circuit breaker state (0=Closed, 1=HalfOpen, 2=Open)",
        new GaugeConfiguration
        {
            LabelNames = new[] { "pipeline" }
        });

    public ResiliencePipeline DatabasePipeline => GetPipeline("Database");

    public ResiliencePipeline RedisPipeline => GetPipeline("Redis");

    public ResiliencePipeline HttpPipeline => GetPipeline("Http");

    public ResiliencePipeline GetPipeline(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException($"{nameof(name)} cannot be null or whitespace", nameof(name));
        }

        return _pipelines.GetOrAdd(name, key => BuildPipeline(SettingsFor(key)));
    }

    private PipelineSettings SettingsFor(string name)
    {
        var configuration = LoadConfiguration();

        if (configuration.Pipelines.TryGetValue(name, out var configured))
        {
            return ProjectSettings(name, configured);
        }

        if (_builtInDefaults.TryGetValue(name, out var fallback))
        {
            return ProjectSettings(name, fallback);
        }

        throw new ArgumentOutOfRangeException(
            nameof(name),
            name,
            $"No resilience pipeline is configured for '{name}'. Add a 'Resilience:Pipelines:{name}' section.");
    }

    private static readonly Dictionary<string, PipelineResilienceConfig> _builtInDefaults =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Database"] = new PipelineResilienceConfig
            {
                MaxRetries = 3,
                RetryDelayMilliseconds = 1000,
                TimeoutSeconds = 30,
                IncludeCircuitBreaker = true,
                CircuitBreakerThreshold = 0.5,
                MinimumThroughput = 10,
                SamplingDurationSeconds = 30,
                BreakDurationSeconds = 60
            },
            ["Redis"] = new PipelineResilienceConfig
            {
                MaxRetries = 3,
                RetryDelayMilliseconds = 100,
                TimeoutSeconds = 10,
                IncludeCircuitBreaker = true,
                CircuitBreakerThreshold = 0.5,
                MinimumThroughput = 10,
                SamplingDurationSeconds = 30,
                BreakDurationSeconds = 30
            },
            ["Http"] = new PipelineResilienceConfig
            {
                MaxRetries = 3,
                RetryDelayMilliseconds = 2000,
                TimeoutSeconds = 60,
                IncludeCircuitBreaker = true,
                CircuitBreakerThreshold = 0.5,
                MinimumThroughput = 5,
                SamplingDurationSeconds = 60,
                BreakDurationSeconds = 120
            }
        };

    private readonly struct PipelineSettings
    {
        public required string Name { get; init; }
        public required int MaxRetries { get; init; }
        public required int RetryDelayMilliseconds { get; init; }
        public required int TimeoutSeconds { get; init; }
        public required int TotalTimeoutSeconds { get; init; }
        public required DelayBackoffType BackoffType { get; init; }
        public required bool UseJitter { get; init; }
        public required bool IncludeCircuitBreaker { get; init; }
        public double CircuitBreakerThreshold { get; init; }
        public int MinimumThroughput { get; init; }
        public int SamplingDurationSeconds { get; init; }
        public int BreakDurationSeconds { get; init; }
        public int MaxConcurrency { get; init; }
        public int ConcurrencyQueueLimit { get; init; }
    }

    private ResilienceConfiguration LoadConfiguration()
    {
        var configuration = new ResilienceConfiguration();
        _configuration.GetSection(ResilienceConfiguration.SectionName).Bind(configuration);
        return configuration;
    }

    private static PipelineSettings ProjectSettings(string name,
                                                    PipelineResilienceConfig config)
    {
        if (config.TimeoutSeconds <= 0)
        {
            throw new InvalidOperationException(
                $"Resilience configuration '{name}.TimeoutSeconds' must be greater than zero but was {config.TimeoutSeconds}.");
        }

        if (config.TotalTimeoutSeconds < 0)
        {
            throw new InvalidOperationException(
                $"Resilience configuration '{name}.TotalTimeoutSeconds' must not be negative but was {config.TotalTimeoutSeconds}.");
        }

        if (config.RetryDelayMilliseconds < 0)
        {
            throw new InvalidOperationException(
                $"Resilience configuration '{name}.RetryDelayMilliseconds' must not be negative but was {config.RetryDelayMilliseconds}.");
        }

        if (config.MaxRetries < 0)
        {
            throw new InvalidOperationException(
                $"Resilience configuration '{name}.MaxRetries' must not be negative but was {config.MaxRetries}.");
        }

        if (config.MaxConcurrency < 0)
        {
            throw new InvalidOperationException(
                $"Resilience configuration '{name}.MaxConcurrency' must not be negative but was {config.MaxConcurrency}.");
        }

        if (config.ConcurrencyQueueLimit < 0)
        {
            throw new InvalidOperationException(
                $"Resilience configuration '{name}.ConcurrencyQueueLimit' must not be negative but was {config.ConcurrencyQueueLimit}.");
        }

        if (config.IncludeCircuitBreaker)
        {
            if (config.CircuitBreakerThreshold < 0.0
                || config.CircuitBreakerThreshold > 1.0)
            {
                throw new InvalidOperationException(
                    $"Resilience configuration '{name}.CircuitBreakerThreshold' must be within [0, 1] but was {config.CircuitBreakerThreshold}.");
            }

            if (config.MinimumThroughput <= 0)
            {
                throw new InvalidOperationException(
                    $"Resilience configuration '{name}.MinimumThroughput' must be greater than zero but was {config.MinimumThroughput}.");
            }

            if (config.SamplingDurationSeconds <= 0)
            {
                throw new InvalidOperationException(
                    $"Resilience configuration '{name}.SamplingDurationSeconds' must be greater than zero but was {config.SamplingDurationSeconds}.");
            }

            if (config.BreakDurationSeconds <= 0)
            {
                throw new InvalidOperationException(
                    $"Resilience configuration '{name}.BreakDurationSeconds' must be greater than zero but was {config.BreakDurationSeconds}.");
            }
        }

        return new PipelineSettings
        {
            Name = name,
            MaxRetries = config.MaxRetries,
            RetryDelayMilliseconds = config.RetryDelayMilliseconds,
            TimeoutSeconds = config.TimeoutSeconds,
            TotalTimeoutSeconds = config.TotalTimeoutSeconds,
            BackoffType = config.LinearBackoff ? DelayBackoffType.Linear : DelayBackoffType.Exponential,
            UseJitter = config.UseJitter,
            IncludeCircuitBreaker = config.IncludeCircuitBreaker,
            CircuitBreakerThreshold = config.CircuitBreakerThreshold,
            MinimumThroughput = config.MinimumThroughput,
            SamplingDurationSeconds = config.SamplingDurationSeconds,
            BreakDurationSeconds = config.BreakDurationSeconds,
            MaxConcurrency = config.MaxConcurrency,
            ConcurrencyQueueLimit = config.ConcurrencyQueueLimit
        };
    }

    private ResiliencePipeline BuildPipeline(PipelineSettings settings)
    {
        var builder = new ResiliencePipelineBuilder();

        if (settings.TotalTimeoutSeconds > 0)
        {
            builder = builder.AddTimeout(TimeSpan.FromSeconds(settings.TotalTimeoutSeconds));
        }

        if (settings.MaxRetries > 0)
        {
            builder = builder
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = settings.MaxRetries,
                    Delay = TimeSpan.FromMilliseconds(settings.RetryDelayMilliseconds),
                    BackoffType = settings.BackoffType,
                    UseJitter = settings.UseJitter,
                    ShouldHandle = BuildTransientFaultPredicate(),
                    OnRetry = args =>
                    {
                        RetriesTotal.WithLabels(settings.Name).Inc();
                        Observe(LogLevel.Warning, values: [("Event", $"{settings.Name} operation retry"), ("AttemptNumber", args.AttemptNumber), ("Delay", args.RetryDelay.TotalMilliseconds)]);
                        return ValueTask.CompletedTask;
                    }
                });
        }

        if (settings.IncludeCircuitBreaker)
        {
            var breakDurationSeconds = settings.BreakDurationSeconds;
            var name = settings.Name;
            builder = builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = settings.CircuitBreakerThreshold,
                MinimumThroughput = settings.MinimumThroughput,
                SamplingDuration = TimeSpan.FromSeconds(settings.SamplingDurationSeconds),
                BreakDuration = TimeSpan.FromSeconds(breakDurationSeconds),
                ShouldHandle = BuildTransientFaultPredicate(),
                OnOpened = args =>
                {
                    CircuitBreakerOpenedTotal.WithLabels(name).Inc();
                    CircuitBreakerState.WithLabels(name).Set(2);
                    Observe(LogLevel.Error, values: [("Event", $"{name} circuit breaker OPENED"), ("BreakDuration", breakDurationSeconds)]);
                    return ValueTask.CompletedTask;
                },
                OnClosed = args =>
                {
                    CircuitBreakerState.WithLabels(name).Set(0);
                    Observe(LogLevel.Information, values: [("Event", $"{name} circuit breaker CLOSED")]);
                    return ValueTask.CompletedTask;
                },
                OnHalfOpened = args =>
                {
                    CircuitBreakerHalfOpenedTotal.WithLabels(name).Inc();
                    CircuitBreakerState.WithLabels(name).Set(1);
                    Observe(LogLevel.Warning, values: [("Event", $"{name} circuit breaker HALF-OPEN (testing recovery)")]);
                    return ValueTask.CompletedTask;
                }
            });
        }

        if (settings.MaxConcurrency > 0)
        {
            builder = builder.AddConcurrencyLimiter(settings.MaxConcurrency,
                                                    settings.ConcurrencyQueueLimit);
        }

        return builder
            .AddTimeout(TimeSpan.FromSeconds(settings.TimeoutSeconds))
            .Build();
    }

    private static PredicateBuilder<object> BuildTransientFaultPredicate()
    {
        return new PredicateBuilder<object>()
            .Handle<TimeoutRejectedException>()
            .Handle<TimeoutException>()
            .Handle<HttpRequestException>()
            .Handle<System.Net.Sockets.SocketException>()
            .Handle<TaskCanceledException>(ex => !ex.CancellationToken.IsCancellationRequested)
            .Handle<InvalidOperationException>(ex =>
                ex.Message.Contains("channel", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("connection", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("not available", StringComparison.OrdinalIgnoreCase));
    }

    [Operation("ExecuteWithResilienceAsync")]
    public async Task<Atelier.Framework.Outcomes.Outcome<T>> ExecuteWithResilienceAsync<T>(
        ResiliencePipeline pipeline,
        Func<CancellationToken, Task<Atelier.Framework.Outcomes.Outcome<T>>> operation,
        string operationName,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Atelier.Framework.Outcomes.Outcome<T>.Failure();
        }
        if (pipeline is null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Pipeline was null"), ("Operation", operationName)]);
            return Atelier.Framework.Outcomes.Outcome<T>.Failure();
        }
        if (operation is null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Operation delegate was null"), ("Operation", operationName)]);
            return Atelier.Framework.Outcomes.Outcome<T>.Failure();
        }
        if (operationName is null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Operation name was null")]);
            return Atelier.Framework.Outcomes.Outcome<T>.Failure();
        }

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "ResilienceOperation", operationName);

        try
        {
            return await pipeline.ExecuteAsync(
                async ct => await operation(ct).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Operation was cancelled"), ("Operation", operationName)]);
            return Atelier.Framework.Outcomes.Outcome<T>.Failure();
        }
        catch (Exception ex)
        {
            ObserveExecutionFailure(ex, operationName);
            return Atelier.Framework.Outcomes.Outcome<T>.Failure();
        }
    }

    private void ObserveExecutionFailure(
        Exception ex,
        string operationName)
    {
        if (ex is BrokenCircuitException)
        {
            Observe(
                LogLevel.Error,
                ex,
                values: [("Reason", "Operation blocked by circuit breaker"), ("Operation", operationName)]);
            return;
        }

        if (ex is TimeoutRejectedException)
        {
            Observe(
                LogLevel.Error,
                ex,
                values: [("Reason", "Operation timed out"), ("Operation", operationName)]);
            return;
        }

        Observe(
            LogLevel.Error,
            ex,
            values: [("Reason", "Resilience pipeline execution failed"), ("Operation", operationName)]);
    }

    [Operation("ExecuteWithResilienceAsync")]
    public async Task<Atelier.Framework.Outcomes.Outcome> ExecuteWithResilienceAsync(
        ResiliencePipeline pipeline,
        Func<CancellationToken, Task<Atelier.Framework.Outcomes.Outcome>> operation,
        string operationName,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Atelier.Framework.Outcomes.Outcome.Failure();
        }
        if (pipeline is null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Pipeline was null"), ("Operation", operationName)]);
            return Atelier.Framework.Outcomes.Outcome.Failure();
        }
        if (operation is null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Operation delegate was null"), ("Operation", operationName)]);
            return Atelier.Framework.Outcomes.Outcome.Failure();
        }
        if (operationName is null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Operation name was null")]);
            return Atelier.Framework.Outcomes.Outcome.Failure();
        }

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "ResilienceOperation", operationName);

        try
        {
            return await pipeline.ExecuteAsync(
                async ct => await operation(ct).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Operation was cancelled"), ("Operation", operationName)]);
            return Atelier.Framework.Outcomes.Outcome.Failure();
        }
        catch (Exception ex)
        {
            ObserveExecutionFailure(ex, operationName);
            return Atelier.Framework.Outcomes.Outcome.Failure();
        }
    }
}
