using Atelier.Framework.Testing;

namespace Atelier.Framework.Strategy;

public static class StrategyFactoryBehaviorTests
{
    [GeneratedTest("Strategy/Registered-Key-Resolves-To-Strategy", "global::Atelier.Framework.Strategy.StrategyFactory")]
    public static void RegisteredKeyResolvesToTheRegisteredStrategy()
    {
        var factory = new StrategyFactory<string, string>(null);
        factory.RegisterStrategy("alpha", "alpha-strategy");

        var resolved = factory.GetStrategy("alpha");

        if (!resolved.IsSuccess)
        {
            throw new InvalidOperationException("resolution of a registered key failed");
        }
        if (resolved.Data != "alpha-strategy")
        {
            throw new InvalidOperationException($"resolved '{resolved.Data}', expected 'alpha-strategy'");
        }
    }

    [GeneratedTest("Strategy/Unknown-Key-Without-Fallback-Fails", "global::Atelier.Framework.Strategy.StrategyFactory")]
    public static void UnknownKeyWithoutFallbackReportsNotFound()
    {
        var factory = new StrategyFactory<string, string>(null);

        var resolved = factory.GetStrategy("missing");

        if (resolved.IsSuccess)
        {
            throw new InvalidOperationException("resolution of an unregistered key without a fallback succeeded");
        }
        if (resolved.Data is not null)
        {
            throw new InvalidOperationException($"a failed resolution carried a strategy: '{resolved.Data}'");
        }
    }

    [GeneratedTest("Strategy/Unknown-Key-Falls-Back-To-Default", "global::Atelier.Framework.Strategy.StrategyFactory")]
    public static void UnknownKeyResolvesToFallbackWhenProvided()
    {
        var factory = new StrategyFactory<string, string>("fallback-strategy");

        var resolved = factory.GetStrategy("missing");

        if (!resolved.IsSuccess)
        {
            throw new InvalidOperationException("resolution with a fallback failed");
        }
        if (resolved.Data != "fallback-strategy")
        {
            throw new InvalidOperationException($"resolved '{resolved.Data}', expected 'fallback-strategy'");
        }
    }

    [GeneratedTest("Strategy/Registered-Key-Wins-Over-Fallback", "global::Atelier.Framework.Strategy.StrategyFactory")]
    public static void RegisteredKeyTakesPrecedenceOverFallback()
    {
        var factory = new StrategyFactory<string, string>("fallback-strategy");
        factory.RegisterStrategy("beta", "beta-strategy");

        var resolved = factory.GetStrategy("beta");

        if (!resolved.IsSuccess)
        {
            throw new InvalidOperationException("resolution of a registered key failed");
        }
        if (resolved.Data != "beta-strategy")
        {
            throw new InvalidOperationException($"resolved '{resolved.Data}', expected the registered 'beta-strategy' to win over the fallback");
        }
    }

    [GeneratedTest("Strategy/Re-Registration-Overwrites-Strategy", "global::Atelier.Framework.Strategy.StrategyFactory")]
    public static void ReRegisteringAKeyOverwritesThePriorStrategy()
    {
        var factory = new StrategyFactory<string, string>(null);
        factory.RegisterStrategy("gamma", "first");
        factory.RegisterStrategy("gamma", "second");

        var resolved = factory.GetStrategy("gamma");

        if (resolved.Data != "second")
        {
            throw new InvalidOperationException($"resolved '{resolved.Data}', expected the overwriting 'second'");
        }
    }

    [GeneratedTest("Strategy/HasStrategy-Reflects-Registration", "global::Atelier.Framework.Strategy.StrategyFactory")]
    public static void HasStrategyReflectsWhetherAKeyIsRegistered()
    {
        var factory = new StrategyFactory<string, string>("fallback-strategy");

        if (factory.HasStrategy("delta"))
        {
            throw new InvalidOperationException("HasStrategy reported a key registered before any registration");
        }

        factory.RegisterStrategy("delta", "delta-strategy");

        if (!factory.HasStrategy("delta"))
        {
            throw new InvalidOperationException("HasStrategy did not report a registered key");
        }
        if (factory.HasStrategy("epsilon"))
        {
            throw new InvalidOperationException("HasStrategy reported an unregistered key as present despite a configured fallback");
        }
    }
}
