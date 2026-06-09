using System.Reflection;

namespace Atelier.Framework.Infrastructure;

public static class SafeTypeResolver
{
    public static Type? Resolve(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        var direct = Type.GetType(
            typeName,
            ResolveLoadedAssembly,
            null,
            throwOnError: false,
            ignoreCase: false);
        if (direct is not null)
        {
            return direct;
        }

        if (typeName.Contains(','))
        {
            return null;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var fromAssembly = assembly.GetType(
                typeName,
                throwOnError: false,
                ignoreCase: false);
            if (fromAssembly is not null)
            {
                return fromAssembly;
            }
        }

        return null;
    }

    public static Type? Resolve(string? typeName, Type requiredAssignableTo)
    {
        ArgumentNullException.ThrowIfNull(requiredAssignableTo);

        var resolved = Resolve(typeName);
        if (resolved is null)
        {
            return null;
        }

        if (!requiredAssignableTo.IsAssignableFrom(resolved))
        {
            return null;
        }

        return resolved;
    }

    private static Assembly? ResolveLoadedAssembly(AssemblyName assemblyName)
    {
        var requested = assemblyName.Name;
        if (string.IsNullOrEmpty(requested))
        {
            return null;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (string.Equals(assembly.GetName().Name, requested, StringComparison.Ordinal))
            {
                return assembly;
            }
        }

        return null;
    }
}
