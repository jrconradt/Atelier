using Atelier.Framework.Outcomes;
using Atelier.Framework.Testing;
using Microsoft.Extensions.Configuration;
using ILogger = Atelier.Framework.Observability.ILogger;

namespace Atelier.Framework.Resilience;

public static class ResiliencePipelineBehaviorTests
{
    private static ResiliencePipelineFactory CreateFactory(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        return new ResiliencePipelineFactory(configuration,
                                             AutoMockProvider.For<ILogger>());
    }

    private static Dictionary<string, string?> PipelineConfig(
        string name,
        params (string Key, string Value)[] values)
    {
        var prefix = $"Resilience:Pipelines:{name}:";
        var result = new Dictionary<string, string?>();
        foreach (var pair in values)
        {
            result[$"{prefix}{pair.Key}"] = pair.Value;
        }
        return result;
    }

    [GeneratedTest("resilience.retry.fires-on-transient-then-succeeds", "global::Atelier.Framework.Resilience.ResiliencePipelineFactory")]
    public static async Task RetryFiresOnTransientFailureThenSucceeds()
    {
        var factory = CreateFactory(
            PipelineConfig(
                "RetryTest",
                ("MaxRetries", "3"),
                ("RetryDelayMilliseconds", "1"),
                ("UseJitter", "false"),
                ("IncludeCircuitBreaker", "false"),
                ("TimeoutSeconds", "30")));

        var attempts = 0;
        var pipeline = factory.GetPipeline("RetryTest");
        var outcome = await factory.ExecuteWithResilienceAsync(
            pipeline,
            _ =>
            {
                attempts++;
                if (attempts < 3)
                {
                    throw new TimeoutException("transient");
                }
                return Task.FromResult(Outcome<int>.Success(42));
            },
            "retry-test",
            CancellationToken.None).ConfigureAwait(false);

        if (!outcome.IsSuccess)
        {
            throw new InvalidOperationException("expected success after retries but observed failure");
        }
        if (outcome.Data != 42)
        {
            throw new InvalidOperationException($"expected the recovered value 42 but got {outcome.Data}");
        }
        if (attempts != 3)
        {
            throw new InvalidOperationException($"expected 3 attempts but observed {attempts}");
        }
    }

    [GeneratedTest("resilience.retry.exhausts-and-reports-transient", "global::Atelier.Framework.Resilience.ResiliencePipelineFactory")]
    public static async Task RetryExhaustsAndReportsTransientFailure()
    {
        var factory = CreateFactory(
            PipelineConfig(
                "RetryExhaustTest",
                ("MaxRetries", "2"),
                ("RetryDelayMilliseconds", "1"),
                ("UseJitter", "false"),
                ("IncludeCircuitBreaker", "false"),
                ("TimeoutSeconds", "30")));

        var attempts = 0;
        var pipeline = factory.GetPipeline("RetryExhaustTest");
        var outcome = await factory.ExecuteWithResilienceAsync(
            pipeline,
            Task<Outcome<int>> (_) =>
            {
                attempts++;
                throw new TimeoutException("transient");
            },
            "retry-exhaust-test",
            CancellationToken.None).ConfigureAwait(false);

        if (outcome.IsSuccess)
        {
            throw new InvalidOperationException("expected failure after retries were exhausted");
        }
        if (attempts != 3)
        {
            throw new InvalidOperationException($"expected 3 attempts (initial + 2 retries) but observed {attempts}");
        }
    }

    [GeneratedTest("resilience.circuit-breaker.opens-after-failures", "global::Atelier.Framework.Resilience.ResiliencePipelineFactory")]
    public static async Task CircuitBreakerOpensAfterRepeatedFailures()
    {
        var factory = CreateFactory(
            PipelineConfig(
                "BreakerTest",
                ("MaxRetries", "0"),
                ("RetryDelayMilliseconds", "1"),
                ("UseJitter", "false"),
                ("IncludeCircuitBreaker", "true"),
                ("CircuitBreakerThreshold", "0.5"),
                ("MinimumThroughput", "2"),
                ("SamplingDurationSeconds", "10"),
                ("BreakDurationSeconds", "30"),
                ("TimeoutSeconds", "30")));

        var pipeline = factory.GetPipeline("BreakerTest");

        for (var i = 0; i < 4; i++)
        {
            await factory.ExecuteWithResilienceAsync(
                pipeline,
                Task<Outcome<int>> (_) => throw new TimeoutException("transient"),
                "breaker-test",
                CancellationToken.None).ConfigureAwait(false);
        }

        var blocked = await factory.ExecuteWithResilienceAsync(
            pipeline,
            _ => Task.FromResult(Outcome<int>.Success(1)),
            "breaker-test",
            CancellationToken.None).ConfigureAwait(false);

        if (blocked.IsSuccess)
        {
            throw new InvalidOperationException("expected the open circuit to block the call");
        }
    }

    [GeneratedTest("resilience.timeout.trips-on-slow-operation", "global::Atelier.Framework.Resilience.ResiliencePipelineFactory")]
    public static async Task TimeoutTripsOnSlowOperation()
    {
        var factory = CreateFactory(
            PipelineConfig(
                "TimeoutTest",
                ("MaxRetries", "0"),
                ("UseJitter", "false"),
                ("IncludeCircuitBreaker", "false"),
                ("TimeoutSeconds", "1")));

        var pipeline = factory.GetPipeline("TimeoutTest");
        var outcome = await factory.ExecuteWithResilienceAsync(
            pipeline,
            async ct =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
                return Outcome<int>.Success(7);
            },
            "timeout-test",
            CancellationToken.None).ConfigureAwait(false);

        if (outcome.IsSuccess)
        {
            throw new InvalidOperationException("expected the per-attempt timeout to trip");
        }
    }

    [GeneratedTest("resilience.total-timeout.bounds-retry-loop", "global::Atelier.Framework.Resilience.ResiliencePipelineFactory")]
    public static async Task TotalTimeoutBoundsRetryLoop()
    {
        var factory = CreateFactory(
            PipelineConfig(
                "TotalTimeoutTest",
                ("MaxRetries", "10"),
                ("RetryDelayMilliseconds", "1000"),
                ("UseJitter", "false"),
                ("IncludeCircuitBreaker", "false"),
                ("TimeoutSeconds", "30"),
                ("TotalTimeoutSeconds", "1")));

        var attempts = 0;
        var pipeline = factory.GetPipeline("TotalTimeoutTest");
        var outcome = await factory.ExecuteWithResilienceAsync(
            pipeline,
            Task<Outcome<int>> (_) =>
            {
                attempts++;
                throw new TimeoutException("transient");
            },
            "total-timeout-test",
            CancellationToken.None).ConfigureAwait(false);

        if (outcome.IsSuccess)
        {
            throw new InvalidOperationException("expected the total timeout to bound the retry loop");
        }
        if (attempts >= 11)
        {
            throw new InvalidOperationException($"expected the total timeout to cut the retry loop short but ran {attempts} attempts");
        }
    }

    [GeneratedTest("resilience.cancellation.reports-cancelled-code", "global::Atelier.Framework.Resilience.ResiliencePipelineFactory")]
    public static async Task CallerCancellationReportsCancelledCode()
    {
        var factory = CreateFactory(
            PipelineConfig(
                "CancelTest",
                ("MaxRetries", "0"),
                ("UseJitter", "false"),
                ("IncludeCircuitBreaker", "false"),
                ("TimeoutSeconds", "30")));

        using var cts = new CancellationTokenSource();
        var pipeline = factory.GetPipeline("CancelTest");
        var outcome = await factory.ExecuteWithResilienceAsync(
            pipeline,
            async ct =>
            {
                cts.Cancel();
                await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
                return Outcome<int>.Success(0);
            },
            "cancel-test",
            cts.Token).ConfigureAwait(false);

        if (outcome.IsSuccess)
        {
            throw new InvalidOperationException("expected caller cancellation to fail the operation");
        }
    }
}
