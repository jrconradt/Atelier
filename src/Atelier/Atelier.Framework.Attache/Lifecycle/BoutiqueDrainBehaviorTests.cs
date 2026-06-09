using Atelier.Framework.Testing;

namespace Atelier.Framework.Attache.Lifecycle;

public static class BoutiqueDrainBehaviorTests
{
    [GeneratedTest("Attache/Drain-Window-Defaults-When-Unset", "global::Atelier.Framework.Attache.Lifecycle.BoutiqueDrain")]
    public static void ResolveDrainWindowFallsBackToDefaultWhenEnvUnset()
    {
        Environment.SetEnvironmentVariable(BoutiqueDrain.DRAIN_SECONDS_ENVIRONMENT_VARIABLE, null);

        var window = BoutiqueDrain.ResolveDrainWindow();
        if (window != TimeSpan.FromSeconds(BoutiqueDrain.DEFAULT_DRAIN_SECONDS))
        {
            throw new InvalidOperationException($"expected default {BoutiqueDrain.DEFAULT_DRAIN_SECONDS}s, got {window.TotalSeconds}s");
        }
    }

    [GeneratedTest("Attache/Drain-Window-Honors-Configured-Seconds", "global::Atelier.Framework.Attache.Lifecycle.BoutiqueDrain")]
    public static void ResolveDrainWindowParsesConfiguredSeconds()
    {
        Environment.SetEnvironmentVariable(BoutiqueDrain.DRAIN_SECONDS_ENVIRONMENT_VARIABLE, "42");

        try
        {
            var window = BoutiqueDrain.ResolveDrainWindow();
            if (window != TimeSpan.FromSeconds(42))
            {
                throw new InvalidOperationException($"expected 42s from env, got {window.TotalSeconds}s");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(BoutiqueDrain.DRAIN_SECONDS_ENVIRONMENT_VARIABLE, null);
        }
    }

    [GeneratedTest("Attache/Drain-Window-Accepts-Zero-To-Disable", "global::Atelier.Framework.Attache.Lifecycle.BoutiqueDrain")]
    public static void ResolveDrainWindowAcceptsZeroAsConfiguredValue()
    {
        Environment.SetEnvironmentVariable(BoutiqueDrain.DRAIN_SECONDS_ENVIRONMENT_VARIABLE, "0");

        try
        {
            var window = BoutiqueDrain.ResolveDrainWindow();
            if (window != TimeSpan.Zero)
            {
                throw new InvalidOperationException($"expected zero drain window, got {window.TotalSeconds}s");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(BoutiqueDrain.DRAIN_SECONDS_ENVIRONMENT_VARIABLE, null);
        }
    }

    [GeneratedTest("Attache/Drain-Window-Rejects-Negative-And-Garbage", "global::Atelier.Framework.Attache.Lifecycle.BoutiqueDrain")]
    public static void ResolveDrainWindowFallsBackOnNegativeOrUnparseable()
    {
        Environment.SetEnvironmentVariable(BoutiqueDrain.DRAIN_SECONDS_ENVIRONMENT_VARIABLE, "-5");

        try
        {
            var negative = BoutiqueDrain.ResolveDrainWindow();
            if (negative != TimeSpan.FromSeconds(BoutiqueDrain.DEFAULT_DRAIN_SECONDS))
            {
                throw new InvalidOperationException($"negative seconds should default, got {negative.TotalSeconds}s");
            }

            Environment.SetEnvironmentVariable(BoutiqueDrain.DRAIN_SECONDS_ENVIRONMENT_VARIABLE, "not-a-number");
            var garbage = BoutiqueDrain.ResolveDrainWindow();
            if (garbage != TimeSpan.FromSeconds(BoutiqueDrain.DEFAULT_DRAIN_SECONDS))
            {
                throw new InvalidOperationException($"unparseable seconds should default, got {garbage.TotalSeconds}s");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(BoutiqueDrain.DRAIN_SECONDS_ENVIRONMENT_VARIABLE, null);
        }
    }
}
