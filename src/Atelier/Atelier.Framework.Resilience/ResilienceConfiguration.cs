using Atelier.Framework.Attributes;

namespace Atelier.Framework.Resilience;

[Contract("ResilienceConfiguration", Version = "1.0", Namespace = "Framework.Resilience")]
public class ResilienceConfiguration
{
    public const string SectionName = "Resilience";

    public Dictionary<string, PipelineResilienceConfig> Pipelines { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

[Contract("PipelineResilienceConfig", Version = "1.0", Namespace = "Framework.Resilience")]
public class PipelineResilienceConfig
{
    public int MaxRetries { get; set; } = 3;
    public int RetryDelayMilliseconds { get; set; } = 1000;
    public int TimeoutSeconds { get; set; } = 30;
    public int TotalTimeoutSeconds { get; set; } = 0;
    public bool LinearBackoff { get; set; } = false;
    public bool UseJitter { get; set; } = true;
    public bool IncludeCircuitBreaker { get; set; } = false;
    public double CircuitBreakerThreshold { get; set; } = 0.5;
    public int MinimumThroughput { get; set; } = 10;
    public int SamplingDurationSeconds { get; set; } = 30;
    public int BreakDurationSeconds { get; set; } = 60;
    public int MaxConcurrency { get; set; } = 0;
    public int ConcurrencyQueueLimit { get; set; } = 0;
}
