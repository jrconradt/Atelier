using Atelier.Framework.Testing;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Atelier.Framework.Attache;

[TestFixtureRegistry]
internal static class AttacheTestFixtures
{
    [Fixture(typeof(HealthCheckService))]
    internal static HealthCheckService HealthCheck()
    {
        return new FixtureHealthCheckService();
    }

    private sealed class FixtureHealthCheckService : HealthCheckService
    {
        public override Task<HealthReport> CheckHealthAsync(
            Func<HealthCheckRegistration, bool>? predicate,
            CancellationToken cancellationToken = default)
        {
            var report = new HealthReport(
                new Dictionary<string, HealthReportEntry>(),
                HealthStatus.Healthy,
                TimeSpan.Zero);

            return Task.FromResult(report);
        }
    }
}
