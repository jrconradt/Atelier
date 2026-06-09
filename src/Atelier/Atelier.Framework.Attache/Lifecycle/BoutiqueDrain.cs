using Atelier.Framework.Observability;
using Microsoft.Extensions.DependencyInjection;

namespace Atelier.Framework.Attache.Lifecycle;

public static class BoutiqueDrain
{
    public const string DRAIN_SECONDS_ENVIRONMENT_VARIABLE = "ATELIER_SHUTDOWN_DRAIN_SECONDS";
    public const int DEFAULT_DRAIN_SECONDS = 15;
    public const int MAX_DRAIN_SECONDS = 300;

    public static TimeSpan ResolveDrainWindow(IAtelier? observer = null)
    {
        var raw = Environment.GetEnvironmentVariable(DRAIN_SECONDS_ENVIRONMENT_VARIABLE);
        if (raw is null)
        {
            return TimeSpan.FromSeconds(DEFAULT_DRAIN_SECONDS);
        }

        if (int.TryParse(raw, out var seconds)
            && seconds >= 0)
        {
            var clamped = Math.Min(seconds, MAX_DRAIN_SECONDS);
            return TimeSpan.FromSeconds(clamped);
        }

        observer?.Observe(LogLevel.Warning,
                          values:
                          [
                              ("Event", "DrainWindowMisconfigured"),
                              (DRAIN_SECONDS_ENVIRONMENT_VARIABLE, raw),
                              ("FallbackSeconds", DEFAULT_DRAIN_SECONDS)
                          ]);

        return TimeSpan.FromSeconds(DEFAULT_DRAIN_SECONDS);
    }

    public static async Task DrainForShutdownAsync(this IServiceProvider services)
    {
        var state = services.GetRequiredService<BoutiqueStartupState>();
        state.BeginDraining();

        var window = ResolveDrainWindow(services.GetService<IAtelier>());
        if (window <= TimeSpan.Zero)
        {
            return;
        }

        await Task.Delay(window).ConfigureAwait(false);
    }
}
