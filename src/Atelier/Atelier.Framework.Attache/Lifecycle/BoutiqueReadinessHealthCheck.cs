using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Atelier.Framework.Attache.Lifecycle;

public sealed class BoutiqueReadinessHealthCheck : IHealthCheck
{
    private readonly BoutiqueStartupState _state;

    public BoutiqueReadinessHealthCheck(BoutiqueStartupState state)
    {
        _state = state;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (_state.IsDraining)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Boutique is draining for shutdown."));
        }

        var result = _state.Result;

        if (!result.HasValue)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Boutique has not finished starting."));
        }

        var outcome = result.Value;
        if (outcome.IsSuccess)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Boutique started."));
        }

        return Task.FromResult(HealthCheckResult.Unhealthy("Boutique startup failed."));
    }
}
