using System.Reflection;
using Atelier.Build.Analysis;
using Atelier.Build.Discovery;
using Atelier.Build.Utils;

namespace Atelier.Build.Generation;

public sealed record InfrastructureRegistration(
    string ServiceType,
    string Implementation,
    string Lifetime,
    bool HasInterface,
    bool IsHostedService);

public static class InfrastructureRegistrationScanner
{
    private static readonly string[] CORE_INFRASTRUCTURE = new[]
    {
        "Atelier.Framework.Infrastructure",
        "Atelier.Framework.Context",
        "Atelier.Framework.Observability",
        "Atelier.Framework.Outcomes",
        "Atelier.Framework.Requisitions",
        "Atelier.Framework.Offering",
        "Atelier.Framework.Attache",
        "Atelier.Framework.Facility",
        "Atelier.Framework.Operation"
    };

    private static readonly string[] LIFETIME_METHODS = { "AddSingleton", "AddScoped", "AddTransient" };

    private const string HOSTED_SERVICE = "Microsoft.Extensions.Hosting.IHostedService";

    public static IReadOnlyList<InfrastructureRegistration> Scan(
        ProductDependencyGraph dependencyGraph,
        string compiledAssembliesDirectory)
    {
        var registrations = new List<InfrastructureRegistration>();

        try
        {
            var resolver = new PathAssemblyResolver(AssemblyPathCatalog.GetAssemblyPaths(compiledAssembliesDirectory));
            using var loadContext = new MetadataLoadContext(resolver);

            var assemblyNames = CORE_INFRASTRUCTURE
                .Union(dependencyGraph.GetAllAssemblies())
                .Distinct()
                .OrderBy(a => a, StringComparer.Ordinal);

            foreach (var assemblyName in assemblyNames)
            {
                var assemblyPath = Path.Combine(compiledAssembliesDirectory, $"{assemblyName}.dll");
                if (!File.Exists(assemblyPath))
                {
                    continue;
                }

                Type[] types;
                try
                {
                    types = loadContext.LoadFromAssemblyPath(assemblyPath).GetTypes();
                }
                catch
                {
                    continue;
                }

                foreach (var type in types)
                {
                    var registration = TryBuild(type);
                    if (registration is not null)
                    {
                        registrations.Add(registration);
                    }
                }
            }
        }
        catch
        {
        }

        return registrations
            .GroupBy(r => $"{r.ServiceType}|{r.Implementation}", StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(r => r.Implementation, StringComparer.Ordinal)
            .ToList();
    }

    private static InfrastructureRegistration? TryBuild(Type type)
    {
        if (!type.IsClass || !type.IsPublic || type.IsAbstract || type.IsGenericTypeDefinition)
        {
            return null;
        }

        var implName = type.FullName;
        if (implName is null || implName.Contains('+') || implName.Contains('`'))
        {
            return null;
        }

        CustomAttributeData? attr;
        try
        {
            attr = type.GetCustomAttributesData()
                .FirstOrDefault(a => a.AttributeType.Name == "InfrastructureAttribute");
        }
        catch
        {
            return null;
        }
        if (attr is null)
        {
            return null;
        }

        var args = attr.ConstructorArguments;
        string? explicitServiceType = null;
        int lifetimeValue;

        if (args.Count == 1)
        {
            lifetimeValue = ToInt(args[0].Value);
        }
        else if (args.Count >= 3)
        {
            explicitServiceType = (args[0].Value as Type)?.FullName;
            lifetimeValue = ToInt(args[2].Value);
        }
        else
        {
            return null;
        }

        var serviceType = explicitServiceType ?? DetermineServiceType(type);
        if (serviceType.Contains('+') || serviceType.Contains('`'))
        {
            serviceType = implName;
        }

        var hasInterface = !string.Equals(serviceType, implName, StringComparison.Ordinal);
        var lifetime = LIFETIME_METHODS[Math.Clamp(lifetimeValue, 0, LIFETIME_METHODS.Length - 1)];
        var isHostedService = lifetimeValue == 0 && ImplementsHostedService(type);

        return new InfrastructureRegistration(serviceType, implName, lifetime, hasInterface, isHostedService);
    }

    private static int ToInt(object? value)
    {
        return value is null ? 0 : Convert.ToInt32(value);
    }

    private static string DetermineServiceType(Type type)
    {
        try
        {
            var candidate = type.GetInterfaces().FirstOrDefault(i => i.Name == $"I{type.Name}");
            return candidate?.FullName ?? type.FullName!;
        }
        catch
        {
            return type.FullName!;
        }
    }

    private static bool ImplementsHostedService(Type type)
    {
        try
        {
            return type.GetInterfaces().Any(i => i.FullName == HOSTED_SERVICE);
        }
        catch
        {
            return false;
        }
    }
}
