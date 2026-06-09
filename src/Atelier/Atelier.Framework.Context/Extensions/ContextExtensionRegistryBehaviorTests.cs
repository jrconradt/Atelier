using Atelier.Framework.Testing;

namespace Atelier.Framework.Context.Extensions;

public static class ContextExtensionRegistryBehaviorTests
{
    [GeneratedTest("Context/ExtensionRegistry-Register-Then-Get-By-Type", "global::Atelier.Framework.Context.Extensions.ContextExtensionRegistry")]
    public static void RegisterThenGetReturnsTheSameInstance()
    {
        var registry = new ContextExtensionRegistry();
        var bag = new DataBagExtension();
        bag.Set("k", "v");

        registry.Register(bag);

        if (!registry.Has<DataBagExtension>())
        {
            throw new InvalidOperationException("Has returned false for a just-registered extension");
        }

        var fetched = registry.Get<DataBagExtension>();
        if (!ReferenceEquals(fetched, bag))
        {
            throw new InvalidOperationException("Get did not return the exact registered instance");
        }
        if (fetched!.Get("k") != "v")
        {
            throw new InvalidOperationException("fetched extension lost its stored state");
        }

        if (registry.Get<ScopeLimiterContextExtension>() is not null)
        {
            throw new InvalidOperationException("Get returned a non-null for an unregistered extension type");
        }
    }

    [GeneratedTest("Context/ExtensionRegistry-Remove-Drops-Extension", "global::Atelier.Framework.Context.Extensions.ContextExtensionRegistry")]
    public static void RemoveDropsTheExtensionFromLookups()
    {
        var registry = new ContextExtensionRegistry();
        registry.Register(new DataBagExtension());

        registry.Remove<DataBagExtension>();

        if (registry.Has<DataBagExtension>())
        {
            throw new InvalidOperationException("extension was still present after Remove");
        }
        if (registry.TryGet<DataBagExtension>(out var dropped)
            || dropped is not null)
        {
            throw new InvalidOperationException("TryGet succeeded after the extension was removed");
        }
    }

    [GeneratedTest("Context/ExtensionRegistry-Clone-Copies-All-Extensions", "global::Atelier.Framework.Context.Extensions.ContextExtensionRegistry")]
    public static void CloneCopiesEveryRegisteredExtension()
    {
        var registry = new ContextExtensionRegistry();
        registry.Register(new DataBagExtension());
        registry.Register(new ScopeLimiterContextExtension());

        var clone = registry.Clone();

        if (clone.GetAll().Count() != 2)
        {
            throw new InvalidOperationException($"expected clone to carry 2 extensions, got {clone.GetAll().Count()}");
        }
        if (!clone.Has<DataBagExtension>()
            || !clone.Has<ScopeLimiterContextExtension>())
        {
            throw new InvalidOperationException("clone dropped one of the registered extension types");
        }
        if (ReferenceEquals(clone.Get<DataBagExtension>(), registry.Get<DataBagExtension>()))
        {
            throw new InvalidOperationException("Clone aliased an extension instance instead of cloning it");
        }
    }

    [GeneratedTest("Context/ExtensionRegistry-Propagation-Clone-Filters-On-Flag", "global::Atelier.Framework.Context.Extensions.ContextExtensionRegistry")]
    public static void CloneWithPropagationKeepsOnlyPropagatingExtensions()
    {
        var registry = new ContextExtensionRegistry();
        registry.Register(new DataBagExtension());
        registry.Register(new ScopeLimiterContextExtension());

        var propagated = registry.CloneWithPropagation();

        if (propagated.Has<DataBagExtension>())
        {
            throw new InvalidOperationException("a non-propagating extension survived CloneWithPropagation");
        }
        if (!propagated.Has<ScopeLimiterContextExtension>())
        {
            throw new InvalidOperationException("a propagating extension was dropped by CloneWithPropagation");
        }
        if (propagated.GetAll().Count() != 1)
        {
            throw new InvalidOperationException($"expected exactly one propagated extension, got {propagated.GetAll().Count()}");
        }
    }
}
