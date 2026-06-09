using Atelier.Framework.Testing;

namespace Atelier.Framework.Performance;

[TestFixtureRegistry]
public static class PerformanceTestFixtures
{
    private const string COMPONENT = "atelier-happy";
    private const string OPERATION = "atelier-happy";
    private const int SAMPLE_COUNT = 32;

    [Fixture(typeof(PerformanceProfiler))]
    public static PerformanceProfiler Profiler()
    {
        var profiler = new PerformanceProfiler();
        var timestamp = DateTime.UtcNow.AddMinutes(1);

        for (var i = 0; i < SAMPLE_COUNT; i++)
        {
            var metric = new PerformanceMetric
            {
                MetricId = Guid.NewGuid().ToString("N"),
                Component = COMPONENT,
                Operation = OPERATION,
                Type = MetricType.Latency,
                Value = 100.0 + i,
                Unit = "ms",
                Timestamp = timestamp
            };

            profiler.RecordMetricAsync(metric).GetAwaiter().GetResult();
        }

        profiler
            .CreateBaselineAsync(
                COMPONENT,
                OPERATION,
                TimeSpan.FromMinutes(5))
            .GetAwaiter()
            .GetResult();

        return profiler;
    }
}
