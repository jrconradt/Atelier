using System.Reflection;
using Atelier.Build.Pipeline;
using Spectre.Console;

namespace Atelier.Build.Analysis;

public class ProductDependencyAnalyzer : IDisposable
{
    private readonly BuildContext _context;
    private const string REQUISITE_ATTRIBUTE_NAME = "RequisiteAttribute";
    private const string INFRASTRUCTURE_ATTRIBUTE_NAME = "InfrastructureAttribute";

    private readonly Dictionary<string, MetadataLoadContext> _loadContexts = new(StringComparer.OrdinalIgnoreCase);

    public ProductDependencyAnalyzer(BuildContext context)
    {
        _context = context;
    }

    private MetadataLoadContext GetLoadContext(string outputDirectory)
    {
        if (_loadContexts.TryGetValue(outputDirectory, out var existing))
        {
            return existing;
        }

        var resolver = new PathAssemblyResolver(AssemblyPathCatalog.GetAssemblyPaths(outputDirectory));
        var loadContext = new MetadataLoadContext(resolver);
        _loadContexts[outputDirectory] = loadContext;
        return loadContext;
    }

    public void Dispose()
    {
        foreach (var loadContext in _loadContexts.Values)
        {
            loadContext.Dispose();
        }

        _loadContexts.Clear();
    }

    public ProductDependencyGraph AnalyzeProduct(
        string productTypeName,
        string productAssemblyName,
        string outputDirectory)
    {
        var graph = new ProductDependencyGraph();
        var assemblyQueue = new Queue<string>();

        var loadContext = GetLoadContext(outputDirectory);

        var productAssemblyPath = Path.Combine(outputDirectory, $"{productAssemblyName}.dll");
        if (!File.Exists(productAssemblyPath))
        {
            if (_context.Verbose)
            {
                AnsiConsole.MarkupLine($"[yellow]Warning:[/] Assembly not found: {productAssemblyPath}");
            }
            return graph;
        }

        graph.AddProduct(new ProductInfo
        {
            TypeName = productTypeName,
            AssemblyName = productAssemblyName,
            AssemblyPath = productAssemblyPath
        });

        graph.AddAssembly(productAssemblyName);
        assemblyQueue.Enqueue(productAssemblyPath);

        var productType = FindProductType(loadContext, productAssemblyPath, productTypeName);
        if (productType != null)
        {
            AnalyzeType(productType,
                        graph,
                        loadContext,
                        outputDirectory,
                        assemblyQueue);
        }

        while (assemblyQueue.Count > 0)
        {
            var currentPath = assemblyQueue.Dequeue();
            ProcessAssemblyDependencies(currentPath,
                                        graph,
                                        loadContext,
                                        outputDirectory,
                                        assemblyQueue);
        }

        if (_context.Verbose)
        {
            AnsiConsole.MarkupLine($"[dim]  Product {productTypeName}: {graph.TotalAssemblyCount} assemblies, {graph.TypeCount} types[/]");
        }

        return graph;
    }

    public ProductDependencyGraph AnalyzeProducts(
        IEnumerable<(string TypeName, string AssemblyName)> products,
        string outputDirectory)
    {
        var mergedGraph = new ProductDependencyGraph();

        foreach (var (typeName, assemblyName) in products)
        {
            var productGraph = AnalyzeProduct(typeName, assemblyName, outputDirectory);
            mergedGraph.Merge(productGraph);
        }

        return mergedGraph;
    }

    private Type? FindProductType(MetadataLoadContext loadContext, string assemblyPath, string productTypeName)
    {
        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);

            var exactMatch = assembly.GetTypes().FirstOrDefault(t =>
                t.FullName == productTypeName ||
                t.Name == productTypeName);

            if (exactMatch != null)
            {
                return exactMatch;
            }

            return assembly.GetTypes().FirstOrDefault(t =>
                t.Name.EndsWith(productTypeName) ||
                (t.FullName?.EndsWith($".{productTypeName}") ?? false));
        }
        catch (Exception ex)
        {
            if (_context.Verbose)
            {
                AnsiConsole.MarkupLine($"[dim]Could not find type {productTypeName}: {ex.Message}[/]");
            }
            return null;
        }
    }

    private void AnalyzeType(
        Type rootType,
        ProductDependencyGraph graph,
        MetadataLoadContext loadContext,
        string outputDirectory,
        Queue<string> assemblyQueue)
    {
        var work = new Stack<Type>();
        work.Push(rootType);

        while (work.Count > 0)
        {
            var type = work.Pop();
            var typeFullName = type.FullName ?? type.Name;

            if (graph.HasVisitedType(typeFullName))
            {
                continue;
            }

            graph.AddVisitedType(typeFullName);
            CollectIsolatedNetwork(type, graph);

            var assemblyName = type.Assembly.GetName().Name;
            if (assemblyName != null && IsAtelierAssembly(assemblyName))
            {
                if (!graph.RequiredAssemblies.Contains(assemblyName))
                {
                    graph.AddAssembly(assemblyName);
                    var assemblyPath = Path.Combine(outputDirectory, $"{assemblyName}.dll");
                    if (File.Exists(assemblyPath))
                    {
                        assemblyQueue.Enqueue(assemblyPath);
                    }
                }
            }

            foreach (var child in CollectRequisiteFieldChildren(type,
                                                                graph,
                                                                loadContext,
                                                                outputDirectory,
                                                                assemblyQueue))
            {
                work.Push(child);
            }

            var baseChild = CollectBaseTypeChild(type,
                                                 graph,
                                                 outputDirectory,
                                                 assemblyQueue);
            if (baseChild != null)
            {
                work.Push(baseChild);
            }

            AnalyzeInterfaces(type, graph);
        }
    }

    private static void CollectIsolatedNetwork(Type type, ProductDependencyGraph graph)
    {
        foreach (var attribute in type.GetCustomAttributesData())
        {
            if (attribute.AttributeType.Name != "NetworkZoneAttribute"
                || attribute.ConstructorArguments.Count < 1)
            {
                continue;
            }

            if (attribute.ConstructorArguments[0].Value is not Type zoneType)
            {
                continue;
            }

            var policy = ReadZonePolicy(zoneType);
            if (policy != null)
            {
                graph.AddZonePolicy(policy);
            }
        }
    }

    private static ZonePolicyInfo? ReadZonePolicy(Type zoneType)
    {
        foreach (var attribute in zoneType.GetCustomAttributesData())
        {
            if (attribute.AttributeType.Name != "ZonePolicyAttribute")
            {
                continue;
            }

            var inbound = new List<string>();
            var outbound = new List<string>();
            var requiresMutualTls = false;
            var isolates = false;

            foreach (var named in attribute.NamedArguments)
            {
                switch (named.MemberName)
                {
                    case "AllowedInbound":
                    {
                        inbound = ReadZoneNames(named.TypedValue);
                        break;
                    }
                    case "AllowedOutbound":
                    {
                        outbound = ReadZoneNames(named.TypedValue);
                        break;
                    }
                    case "RequiresMutualTls":
                    {
                        requiresMutualTls = named.TypedValue.Value is true;
                        break;
                    }
                    case "Isolates":
                    {
                        isolates = named.TypedValue.Value is true;
                        break;
                    }
                }
            }

            return new ZonePolicyInfo(zoneType.Name.ToLowerInvariant(),
                                      inbound,
                                      outbound,
                                      requiresMutualTls,
                                      isolates);
        }

        return null;
    }

    private static List<string> ReadZoneNames(CustomAttributeTypedArgument argument)
    {
        var names = new List<string>();

        if (argument.Value is IReadOnlyList<CustomAttributeTypedArgument> items)
        {
            foreach (var item in items)
            {
                if (item.Value is Type zone)
                {
                    names.Add(zone.Name.ToLowerInvariant());
                }
            }
        }

        return names;
    }

    private IEnumerable<Type> CollectRequisiteFieldChildren(
        Type type,
        ProductDependencyGraph graph,
        MetadataLoadContext loadContext,
        string outputDirectory,
        Queue<string> assemblyQueue)
    {
        var children = new List<Type>();

        try
        {
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var field in fields)
            {
                if (!HasRequisiteAttribute(field))
                {
                    continue;
                }

                var fieldType = field.FieldType;
                var fieldAssemblyName = fieldType.Assembly.GetName().Name;
                var typeFullName = type.FullName ?? type.Name;
                var fieldTypeFullName = fieldType.FullName ?? fieldType.Name;

                graph.AddDependency(typeFullName, fieldTypeFullName, "Requisite");

                if (fieldAssemblyName != null && IsAtelierAssembly(fieldAssemblyName))
                {
                    if (!graph.RequiredAssemblies.Contains(fieldAssemblyName))
                    {
                        graph.AddAssembly(fieldAssemblyName);
                        var assemblyPath = Path.Combine(outputDirectory, $"{fieldAssemblyName}.dll");
                        if (File.Exists(assemblyPath))
                        {
                            assemblyQueue.Enqueue(assemblyPath);
                        }
                    }

                    if (!graph.HasVisitedType(fieldTypeFullName))
                    {
                        var concreteType = TryResolveConcreteType(fieldType, loadContext, outputDirectory);
                        if (concreteType != null)
                        {
                            children.Add(concreteType);
                        }
                    }
                }

                if (fieldType.IsGenericType)
                {
                    foreach (var genericArg in fieldType.GetGenericArguments())
                    {
                        var genericAssemblyName = genericArg.Assembly.GetName().Name;
                        if (genericAssemblyName != null && IsAtelierAssembly(genericAssemblyName))
                        {
                            graph.AddAssembly(genericAssemblyName);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (_context.Verbose)
            {
                AnsiConsole.MarkupLine($"[dim]Error analyzing fields of {type.Name}: {ex.Message}[/]");
            }
        }

        return children;
    }

    private Type? CollectBaseTypeChild(
        Type type,
        ProductDependencyGraph graph,
        string outputDirectory,
        Queue<string> assemblyQueue)
    {
        try
        {
            var baseType = type.BaseType;
            if (baseType == null || baseType.FullName == "System.Object")
            {
                return null;
            }

            var baseAssemblyName = baseType.Assembly.GetName().Name;
            if (baseAssemblyName != null && IsAtelierAssembly(baseAssemblyName))
            {
                var typeFullName = type.FullName ?? type.Name;
                var baseTypeFullName = baseType.FullName ?? baseType.Name;

                graph.AddDependency(typeFullName, baseTypeFullName, "BaseType");
                if (!graph.RequiredAssemblies.Contains(baseAssemblyName))
                {
                    graph.AddAssembly(baseAssemblyName);
                    var assemblyPath = Path.Combine(outputDirectory, $"{baseAssemblyName}.dll");
                    if (File.Exists(assemblyPath))
                    {
                        assemblyQueue.Enqueue(assemblyPath);
                    }
                }

                return baseType;
            }

            return null;
        }
        catch (Exception ex)
        {
            if (_context.Verbose)
            {
                AnsiConsole.MarkupLine($"[dim]Error analyzing base type of {type.Name}: {ex.Message}[/]");
            }
            return null;
        }
    }

    private void AnalyzeInterfaces(Type type, ProductDependencyGraph graph)
    {
        try
        {
            foreach (var iface in type.GetInterfaces())
            {
                var ifaceAssemblyName = iface.Assembly.GetName().Name;
                if (ifaceAssemblyName != null && IsAtelierAssembly(ifaceAssemblyName))
                {
                    var typeFullName = type.FullName ?? type.Name;
                    var ifaceFullName = iface.FullName ?? iface.Name;

                    graph.AddDependency(typeFullName, ifaceFullName, "Interface");
                    graph.AddAssembly(ifaceAssemblyName);
                }
            }
        }
        catch (Exception ex)
        {
            if (_context.Verbose)
            {
                AnsiConsole.MarkupLine($"[dim]Error analyzing interfaces of {type.Name}: {ex.Message}[/]");
            }
        }
    }

    private void ProcessAssemblyDependencies(
        string assemblyPath,
        ProductDependencyGraph graph,
        MetadataLoadContext loadContext,
        string outputDirectory,
        Queue<string> assemblyQueue)
    {
        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);

            foreach (var refAssembly in assembly.GetReferencedAssemblies())
            {
                var refName = refAssembly.Name;
                if (refName != null && IsAtelierAssembly(refName) && !graph.RequiredAssemblies.Contains(refName))
                {
                    var refPath = Path.Combine(outputDirectory, $"{refName}.dll");
                    if (File.Exists(refPath))
                    {
                        graph.AddAssembly(refName);
                        assemblyQueue.Enqueue(refPath);
                    }
                }
            }

            foreach (var type in assembly.GetTypes())
            {
                if (HasInfrastructureAttribute(type) && !graph.HasVisitedType(type.FullName ?? type.Name))
                {
                    AnalyzeType(type,
                                graph,
                                loadContext,
                                outputDirectory,
                                assemblyQueue);
                }
            }
        }
        catch (Exception ex)
        {
            if (_context.Verbose)
            {
                AnsiConsole.MarkupLine($"[dim]Error processing assembly {Path.GetFileName(assemblyPath)}: {ex.Message}[/]");
            }
        }
    }

    private Type? TryResolveConcreteType(
        Type interfaceType,
        MetadataLoadContext loadContext,
        string outputDirectory)
    {
        if (!interfaceType.IsInterface)
        {
            return interfaceType;
        }

        try
        {
            var assemblyName = interfaceType.Assembly.GetName().Name;
            if (assemblyName == null)
            {
                return null;
            }

            var assemblyPath = Path.Combine(outputDirectory, $"{assemblyName}.dll");
            if (!File.Exists(assemblyPath))
            {
                return null;
            }

            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);

            var implementers = assembly.GetTypes()
                .Where(t =>
                    !t.IsInterface
                    && !t.IsAbstract
                    && t.GetInterfaces().Any(i => i.FullName == interfaceType.FullName))
                .ToList();

            if (implementers.Count == 1)
            {
                return implementers[0];
            }

            if (implementers.Count > 1
                && _context.Verbose)
            {
                AnsiConsole.MarkupLine(
                    $"[yellow]Ambiguous implementation for {interfaceType.FullName}: {string.Join(", ", implementers.Select(t => t.FullName))}[/]");
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static bool HasRequisiteAttribute(MemberInfo member)
    {
        try
        {
            return member.CustomAttributes.Any(attr =>
                attr.AttributeType.Name == REQUISITE_ATTRIBUTE_NAME);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasInfrastructureAttribute(Type type)
    {
        try
        {
            return type.CustomAttributes.Any(attr =>
                attr.AttributeType.Name == INFRASTRUCTURE_ATTRIBUTE_NAME);
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
