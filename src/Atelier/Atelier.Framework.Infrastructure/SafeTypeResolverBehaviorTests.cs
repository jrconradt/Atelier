using Atelier.Framework.Testing;

namespace Atelier.Framework.Infrastructure;

public static class SafeTypeResolverBehaviorTests
{
    private interface IResolverProbe
    {
    }

    private sealed class ResolverProbe : IResolverProbe
    {
    }

    private sealed class UnrelatedProbe
    {
    }

    [GeneratedTest("Infrastructure/SafeTypeResolver-Resolves-Loaded-Assignable-Type", "global::Atelier.Framework.Infrastructure.SafeTypeResolver")]
    public static Task ResolvesLoadedTypeAssignableToRequiredBase()
    {
        var resolved = SafeTypeResolver.Resolve(typeof(ResolverProbe).AssemblyQualifiedName, typeof(IResolverProbe));
        if (resolved != typeof(ResolverProbe))
        {
            throw new InvalidOperationException($"expected ResolverProbe to resolve, got '{resolved?.FullName ?? "null"}'");
        }

        return Task.CompletedTask;
    }

    [GeneratedTest("Infrastructure/SafeTypeResolver-Rejects-Non-Assignable-Type", "global::Atelier.Framework.Infrastructure.SafeTypeResolver")]
    public static Task ReturnsNullWhenLoadedTypeNotAssignableToRequiredBase()
    {
        var resolved = SafeTypeResolver.Resolve(typeof(UnrelatedProbe).AssemblyQualifiedName, typeof(IResolverProbe));
        if (resolved is not null)
        {
            throw new InvalidOperationException($"expected null for a type not assignable to the required base, got '{resolved.FullName}'");
        }

        return Task.CompletedTask;
    }

    [GeneratedTest("Infrastructure/SafeTypeResolver-Rejects-Unloaded-Assembly", "global::Atelier.Framework.Infrastructure.SafeTypeResolver")]
    public static Task ReturnsNullForUnknownAssembly()
    {
        var resolved = SafeTypeResolver.Resolve("Some.Unloaded.Type, Some.Unloaded.Assembly");
        if (resolved is not null)
        {
            throw new InvalidOperationException($"expected null for a type in an unloaded assembly, got '{resolved.FullName}'");
        }

        return Task.CompletedTask;
    }

    [GeneratedTest("Infrastructure/SafeTypeResolver-Rejects-Null-Or-Whitespace", "global::Atelier.Framework.Infrastructure.SafeTypeResolver")]
    public static Task ReturnsNullForNullOrWhitespaceTypeName()
    {
        if (SafeTypeResolver.Resolve(null) is not null)
        {
            throw new InvalidOperationException("expected null for a null type name");
        }

        if (SafeTypeResolver.Resolve("   ") is not null)
        {
            throw new InvalidOperationException("expected null for a whitespace type name");
        }

        return Task.CompletedTask;
    }
}
