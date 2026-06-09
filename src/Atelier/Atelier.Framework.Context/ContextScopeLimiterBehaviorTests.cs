using Atelier.Framework.Testing;

namespace Atelier.Framework.Context;

public static class ContextScopeLimiterBehaviorTests
{
    [GeneratedTest("Context/ScopeLimiter-AllowAll-Permits-Any-Key", "global::Atelier.Framework.Context.ContextScopeLimiter")]
    public static void AllowAllPermitsArbitraryKeysOperationsAndScopes()
    {
        var limiter = ContextScopeLimiter.Create();

        if (!limiter.IsAllowAll)
        {
            throw new InvalidOperationException("Create() did not produce an allow-all limiter");
        }
        if (!limiter.IsDataKeyAllowed("anything"))
        {
            throw new InvalidOperationException("allow-all limiter rejected an arbitrary data key");
        }
        if (!limiter.IsOperationAllowed("Whatever"))
        {
            throw new InvalidOperationException("allow-all limiter rejected an arbitrary operation");
        }
        if (!limiter.IsScopeAllowed(ContextScope.System))
        {
            throw new InvalidOperationException("allow-all limiter rejected an arbitrary scope");
        }
    }

    [GeneratedTest("Context/ScopeLimiter-Allowlist-Restricts-To-Listed-Keys", "global::Atelier.Framework.Context.ContextScopeLimiter")]
    public static void AllowlistRestrictsToListedKeysOnly()
    {
        var limiter = ContextScopeLimiter.ForScope(ContextScope.Operation);

        if (!limiter.IsDataKeyAllowed("Input"))
        {
            throw new InvalidOperationException("operation-scoped limiter rejected the allowlisted key 'Input'");
        }
        if (limiter.IsDataKeyAllowed("ServiceId"))
        {
            throw new InvalidOperationException("operation-scoped limiter accepted 'ServiceId', which is outside its allowlist");
        }
        if (!limiter.IsOperationAllowed("Execute"))
        {
            throw new InvalidOperationException("operation-scoped limiter rejected the allowlisted operation 'Execute'");
        }
        if (limiter.IsOperationAllowed("Administer"))
        {
            throw new InvalidOperationException("operation-scoped limiter accepted 'Administer', which is outside its allowlist");
        }
        if (limiter.IsScopeAllowed(ContextScope.System))
        {
            throw new InvalidOperationException("operation-scoped limiter accepted the System scope, which is outside its allowlist");
        }
    }

    [GeneratedTest("Context/ScopeLimiter-Block-Overrides-Allow", "global::Atelier.Framework.Context.ContextScopeLimiter")]
    public static void BlockedKeyIsDeniedEvenWhenAllowAll()
    {
        var limiter = ContextScopeLimiter.Create()
            .BlockDataKeys("Secret")
            .BlockOperations("Drop")
            .BlockScopes(ContextScope.External);

        if (limiter.IsDataKeyAllowed("Secret"))
        {
            throw new InvalidOperationException("blocked key 'Secret' was permitted under allow-all");
        }
        if (limiter.IsOperationAllowed("Drop"))
        {
            throw new InvalidOperationException("blocked operation 'Drop' was permitted under allow-all");
        }
        if (limiter.IsScopeAllowed(ContextScope.External))
        {
            throw new InvalidOperationException("blocked scope External was permitted under allow-all");
        }
        if (!limiter.IsDataKeyAllowed("Other"))
        {
            throw new InvalidOperationException("non-blocked key 'Other' was wrongly denied under allow-all");
        }
    }

    [GeneratedTest("Context/ScopeLimiter-Constraints-Roundtrip-Typed", "global::Atelier.Framework.Context.ContextScopeLimiter")]
    public static void ConstraintsRoundtripWithTypeMatching()
    {
        var limiter = ContextScopeLimiter.Create()
            .AddConstraint("max-depth", 7)
            .AddConstraint("region", "us-east");

        if (!limiter.HasConstraint("max-depth"))
        {
            throw new InvalidOperationException("HasConstraint did not see the registered 'max-depth' constraint");
        }
        if (limiter.GetConstraint<int>("max-depth") != 7)
        {
            throw new InvalidOperationException($"expected constraint 'max-depth' to read back 7, got {limiter.GetConstraint<int>("max-depth")}");
        }
        if (limiter.GetConstraint<string>("region") != "us-east")
        {
            throw new InvalidOperationException($"expected constraint 'region' to read back 'us-east', got '{limiter.GetConstraint<string>("region")}'");
        }
        if (limiter.GetConstraint<string>("max-depth") is not null)
        {
            throw new InvalidOperationException("type-mismatched read of 'max-depth' as string returned a non-default value");
        }
    }

    [GeneratedTest("Context/ScopeLimiter-Clone-Is-Independent", "global::Atelier.Framework.Context.ContextScopeLimiter")]
    public static void ClonePreservesRulesAndIsIndependentOfSource()
    {
        var source = ContextScopeLimiter.ForScope(ContextScope.Service)
            .AddConstraint("tier", "gold");

        var clone = source.Clone();

        if (!clone.IsDataKeyAllowed("ServiceId"))
        {
            throw new InvalidOperationException("clone lost the allowlisted key 'ServiceId'");
        }
        if (clone.GetConstraint<string>("tier") != "gold")
        {
            throw new InvalidOperationException("clone lost the 'tier' constraint");
        }

        source.AllowDataKeys("LateAddition");

        if (clone.IsDataKeyAllowed("LateAddition"))
        {
            throw new InvalidOperationException("mutation of the source limiter leaked into the clone");
        }
    }
}
