using System.Reflection;
using Atelier.Build.Pipeline;
using Spectre.Console;

namespace Atelier.Build.Analysis;

public class RequisiteAnalyzer
{
    private readonly BuildContext _context;
    private const string REQUISITE_ATTRIBUTE_NAME = "RequisiteAttribute";
    private const string RUNTIME_ATTRIBUTE_NAME = "RuntimeAttribute";

    public RequisiteAnalyzer(BuildContext context)
    {
        _context = context;
    }

    public RequisiteAssemblies AnalyzeRequiredAssemblies(string outputDirectory, string boutiqueAssemblyPath)
    {
        var requiredAssemblies = new RequisiteAssemblies();
        var processedAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assemblyQueue = new Queue<string>();

        var resolver = new PathAssemblyResolver(AssemblyPathCatalog.GetAssemblyPaths(outputDirectory));
        using var loadContext = new MetadataLoadContext(resolver);

        var boutiqueDllName = Path.GetFileName(boutiqueAssemblyPath);
        assemblyQueue.Enqueue(boutiqueAssemblyPath);
        requiredAssemblies.Add(boutiqueDllName);

        while (assemblyQueue.Count > 0)
        {
            var currentPath = assemblyQueue.Dequeue();
            var currentDllName = Path.GetFileName(currentPath);

            if (processedAssemblies.Contains(currentDllName))
            {
                continue;
            }

            processedAssemblies.Add(currentDllName);

            try
            {
                var assembly = loadContext.LoadFromAssemblyPath(currentPath);

                var referencedAssemblies = GetAllReferencedAssemblies(assembly);
                foreach (var refName in referencedAssemblies)
                {
                    var refDllName = $"{refName}.dll";
                    if (!requiredAssemblies.Contains(refDllName))
                    {
                        var refPath = Path.Combine(outputDirectory, refDllName);
                        if (File.Exists(refPath))
                        {
                            requiredAssemblies.Add(refDllName, refPath);
                            assemblyQueue.Enqueue(refPath);
                        }
                    }
                }

                var requisiteDependencies = AnalyzeAssemblyRequisites(assembly);
                foreach (var depAssemblyName in requisiteDependencies)
                {
                    var depDllName = $"{depAssemblyName}.dll";
                    if (!requiredAssemblies.Contains(depDllName))
                    {
                        var depPath = Path.Combine(outputDirectory, depDllName);
                        if (File.Exists(depPath))
                        {
                            requiredAssemblies.Add(depDllName, depPath);
                            assemblyQueue.Enqueue(depPath);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (_context.Verbose)
                {
                    AnsiConsole.MarkupLine($"[dim]Skipping {currentDllName}: {ex.Message}[/]");
                }
            }
        }

        return requiredAssemblies;
    }

    private static IEnumerable<string> GetAllReferencedAssemblies(Assembly assembly)
    {
        return assembly.GetReferencedAssemblies()
            .Where(r => !IsSystemAssembly(r.Name ?? string.Empty))
            .Select(r => r.Name!)
            .Distinct();
    }

    private static bool IsSystemAssembly(string assemblyName)
    {
        return assemblyName.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
               assemblyName.StartsWith("Microsoft.CSharp", StringComparison.OrdinalIgnoreCase) ||
               assemblyName.Equals("mscorlib", StringComparison.OrdinalIgnoreCase) ||
               assemblyName.Equals("netstandard", StringComparison.OrdinalIgnoreCase) ||
               assemblyName.StartsWith("System", StringComparison.OrdinalIgnoreCase) &&
               !assemblyName.Contains(".");
    }

    private static IEnumerable<string> AnalyzeAssemblyRequisites(Assembly assembly)
    {
        var dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var type in assembly.GetTypes())
        {
            if (!IsAtelierType(type))
            {
                continue;
            }

            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (HasRequisiteAttribute(field))
                {
                    var fieldTypeName = GetAssemblyNameFromType(field.FieldType);
                    if (fieldTypeName != null && IsAtelierAssembly(fieldTypeName))
                    {
                        dependencies.Add(fieldTypeName);
                    }
                }
            }

            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (HasRequisiteAttribute(property))
                {
                    var propertyTypeName = GetAssemblyNameFromType(property.PropertyType);
                    if (propertyTypeName != null && IsAtelierAssembly(propertyTypeName))
                    {
                        dependencies.Add(propertyTypeName);
                    }
                }
            }
        }

        return dependencies;
    }

    private static string? GetAssemblyNameFromType(Type type)
    {
        try
        {
            if (type.IsGenericType && !type.IsGenericTypeDefinition)
            {
                foreach (var genericArg in type.GetGenericArguments())
                {
                    var argAssembly = genericArg.Assembly.GetName().Name;
                    if (argAssembly != null && IsAtelierAssembly(argAssembly))
                    {
                        return argAssembly;
                    }
                }
            }

            return type.Assembly.GetName().Name;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsAtelierType(Type type)
    {
        try
        {
            return type.GetInterfaces().Any(i => i.Name == "IAtelier");
        }
        catch
        {
            return false;
        }
    }

    private static bool HasRequisiteAttribute(MemberInfo member)
    {
        try
        {
            return member.CustomAttributes.Any(attr =>
                attr.AttributeType.Name == REQUISITE_ATTRIBUTE_NAME ||
                attr.AttributeType.Name == RUNTIME_ATTRIBUTE_NAME);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsAtelierAssembly(string assemblyName)
    {
        return assemblyName.StartsWith("Atelier.", StringComparison.OrdinalIgnoreCase);
    }

}
