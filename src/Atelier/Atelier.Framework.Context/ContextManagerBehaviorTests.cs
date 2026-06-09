using Atelier.Framework.Testing;

namespace Atelier.Framework.Context;

public static class ContextManagerBehaviorTests
{
    [GeneratedTest("Context/Manager-Create-Registers-Active-For-Service", "global::Atelier.Framework.Context.ContextManager")]
    public static async Task CreateThenEvolveSurfacesContextInActiveServiceQuery()
    {
        using var manager = new ContextManager();

        var context = await manager.CreateContextAsync(
            "checkout",
            ContextScope.Service,
            serviceId: "orders",
            domainId: "commerce").ConfigureAwait(false);

        var beforeEvolve = await manager.GetActiveContextsForServiceAsync("orders").ConfigureAwait(false);
        if (beforeEvolve.Any())
        {
            throw new InvalidOperationException("a freshly-created context was reported Active before EvolveToRuntime");
        }

        manager.EvolveToRuntime(context);

        if (context.Lifecycle != ContextLifecycle.Active)
        {
            throw new InvalidOperationException($"expected lifecycle Active after EvolveToRuntime, got {context.Lifecycle}");
        }
        if (context.IsCompileTime)
        {
            throw new InvalidOperationException("context remained compile-time after EvolveToRuntime");
        }

        var afterEvolve = await manager.GetActiveContextsForServiceAsync("orders").ConfigureAwait(false);
        if (afterEvolve.Count() != 1)
        {
            throw new InvalidOperationException($"expected exactly one active context for 'orders', got {afterEvolve.Count()}");
        }
        if (afterEvolve.Single().ContextId != context.ContextId)
        {
            throw new InvalidOperationException("active-service query returned a different context than the one created");
        }

        var otherDomain = await manager.GetActiveContextsForDomainAsync("unrelated").ConfigureAwait(false);
        if (otherDomain.Any())
        {
            throw new InvalidOperationException("domain query returned contexts for a domain that has none");
        }
    }

    [GeneratedTest("Context/Manager-Finalize-Walks-Hierarchy-And-Deregisters", "global::Atelier.Framework.Context.ContextManager")]
    public static async Task FinalizeCompletesEntireHierarchyAndClearsRegistry()
    {
        using var manager = new ContextManager();

        var root = await manager.CreateContextAsync(
            "root",
            ContextScope.System,
            serviceId: "svc").ConfigureAwait(false);

        var child = root.CreateChild("child", ContextScope.Service);
        var grandchild = child.CreateChild("grandchild", ContextScope.Operation);

        await manager.FinalizeContextAsync(root, ContextStatus.Success).ConfigureAwait(false);

        if (root.Lifecycle != ContextLifecycle.Completed
            || child.Lifecycle != ContextLifecycle.Completed
            || grandchild.Lifecycle != ContextLifecycle.Completed)
        {
            throw new InvalidOperationException("Finalize did not drive every node in the hierarchy to Completed");
        }
        if (root.Status != ContextStatus.Success
            || grandchild.Status != ContextStatus.Success)
        {
            throw new InvalidOperationException("Finalize did not stamp the requested final status across the hierarchy");
        }

        var hierarchy = await manager.GetContextHierarchyAsync(root.ContextId).ConfigureAwait(false);
        if (hierarchy.Any())
        {
            throw new InvalidOperationException("root remained resolvable in the registry after finalization");
        }
    }

    [GeneratedTest("Context/Manager-Evicts-Only-Expired-Roots", "global::Atelier.Framework.Context.ContextManager")]
    public static async Task EvictExpiredRemovesPastDueRootsAndLeavesLiveOnes()
    {
        using var manager = new ContextManager();

        var expired = await manager.CreateContextAsync("expired", ContextScope.Operation).ConfigureAwait(false);
        expired.ExpiresAt = DateTime.UtcNow.AddMinutes(-5);
        manager.EvolveToRuntime(expired);

        var live = await manager.CreateContextAsync("live", ContextScope.Operation).ConfigureAwait(false);
        live.ExpiresAt = DateTime.UtcNow.AddMinutes(5);
        live.ServiceId = "keep";
        manager.EvolveToRuntime(live);

        var evicted = await manager.EvictExpiredContextsAsync().ConfigureAwait(false);
        if (evicted != 1)
        {
            throw new InvalidOperationException($"expected exactly one expired root evicted, got {evicted}");
        }
        if (expired.Lifecycle != ContextLifecycle.Completed)
        {
            throw new InvalidOperationException("evicted context was not finalized to Completed");
        }

        var remaining = await manager.GetContextHierarchyAsync(live.ContextId).ConfigureAwait(false);
        if (live.Lifecycle != ContextLifecycle.Active)
        {
            throw new InvalidOperationException("a non-expired context was wrongly finalized during eviction");
        }
    }

    [GeneratedTest("Context/Manager-CrossService-Stamps-Provenance-Metadata", "global::Atelier.Framework.Context.ContextManager")]
    public static async Task CrossServiceContextCarriesSourceTargetAndOperationMetadata()
    {
        using var manager = new ContextManager();

        var parent = await manager.CreateContextAsync(
            "parent",
            ContextScope.Service,
            serviceId: "source-svc",
            domainId: "source-dom").ConfigureAwait(false);

        var crossed = manager.CreateCrossServiceContext(
            parent,
            "target-svc",
            "target-dom",
            "Replicate");

        if (crossed.ServiceId != "target-svc"
            || crossed.DomainId != "target-dom")
        {
            throw new InvalidOperationException("cross-service context did not retarget service/domain identifiers");
        }
        if (!crossed.ServiceMetadata.TryGetValue("cross-offering:source-offering", out var src)
            || src != "source-svc")
        {
            throw new InvalidOperationException($"cross-service metadata source was '{src}', expected 'source-svc'");
        }
        if (!crossed.ServiceMetadata.TryGetValue("cross-offering:target-offering", out var tgt)
            || tgt != "target-svc")
        {
            throw new InvalidOperationException($"cross-service metadata target was '{tgt}', expected 'target-svc'");
        }
        if (!crossed.ServiceMetadata.TryGetValue("cross-offering:operation", out var op)
            || op != "Replicate")
        {
            throw new InvalidOperationException($"cross-service metadata operation was '{op}', expected 'Replicate'");
        }
        if (crossed.CorrelationId != parent.CorrelationId)
        {
            throw new InvalidOperationException("cross-service context did not inherit the parent correlation id");
        }
    }

    [GeneratedTest("Context/Manager-Validate-Requirements-Gates-On-CompileTime-Keys", "global::Atelier.Framework.Context.ContextManager")]
    public static async Task ValidateRequirementsRequiresCompileTimeAndPresentKeys()
    {
        using var manager = new ContextManager();

        var context = await manager.CreateContextAsync("compile", ContextScope.Operation).ConfigureAwait(false);
        context.AddValue("Input", "payload");

        if (!manager.ValidateContextRequirements(context, new[] { "Input" }))
        {
            throw new InvalidOperationException("validation failed for a compile-time context that holds the required key");
        }
        if (manager.ValidateContextRequirements(context, new[] { "Input", "Missing" }))
        {
            throw new InvalidOperationException("validation passed despite a missing required key");
        }

        manager.EvolveToRuntime(context);

        if (manager.ValidateContextRequirements(context, new[] { "Input" }))
        {
            throw new InvalidOperationException("validation passed for a runtime context, which the gate should reject");
        }
    }
}
