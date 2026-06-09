using System.Reflection;
using Atelier.Build.Analysis;
using Atelier.Build.Discovery;
using Atelier.Build.Pipeline;
using Atelier.Build.Utils;
using Spectre.Console;
using Templar.Rendering;
using T = Atelier.Build.Templates.Stubs;

namespace Atelier.Build.Generation;

public class PerBoutiqueAssemblyLoaderGenerator
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

    private readonly BuildContext _context;

    public PerBoutiqueAssemblyLoaderGenerator(BuildContext context)
    {
        _context = context;
    }

    public async Task<string> GenerateAsync(
        BoutiqueYamlSchema schema,
        ProductDependencyGraph dependencyGraph,
        string outputDirectory,
        string compiledAssembliesDirectory)
    {
        var boutiqueName = Naming.ToBoutiqueAssemblyIdentifier(schema.Name);

        var typeTouches = FindInfrastructureTypes(dependencyGraph, compiledAssembliesDirectory);
        var boutiqueAssemblies = dependencyGraph.GetAllAssemblies().ToList();
        var allAssemblies = CORE_INFRASTRUCTURE.Union(boutiqueAssemblies).Distinct().OrderBy(a => a);

        var assemblyLoads = Sequence.Lines(allAssemblies.Select(a => (Compositor)new T.AssemblyLoadLine { Name = a }));

        var typeTouchBlock = Sequence.Lines(typeTouches.Select(tt => (Compositor)new T.TypeTouchLine { TypeName = tt }));

        var code = new T.AssemblyLoader
        {
            BoutiqueName = boutiqueName,
            AssemblyLoads = assemblyLoads,
            TypeTouches = typeTouchBlock,
        }.Render();

        var outputPath = Path.Combine(outputDirectory, $"AssemblyLoader{boutiqueName}.g.cs");
        await File.WriteAllTextAsync(outputPath, code).ConfigureAwait(false);

        if (_context.Verbose)
        {
            AnsiConsole.MarkupLine($"[dim]    → Generated AssemblyLoader{boutiqueName}.g.cs ({typeTouches.Count} type touches)[/]");
        }

        return outputPath;
    }

    private List<string> FindInfrastructureTypes(
        ProductDependencyGraph dependencyGraph,
        string compiledAssembliesDirectory)
    {
        var types = new List<string>();

        try
        {
            var resolver = new PathAssemblyResolver(AssemblyPathCatalog.GetAssemblyPaths(compiledAssembliesDirectory));
            using var loadContext = new MetadataLoadContext(resolver);

            foreach (var assemblyName in dependencyGraph.GetAllAssemblies())
            {
                var assemblyPath = Path.Combine(compiledAssembliesDirectory, $"{assemblyName}.dll");
                if (!File.Exists(assemblyPath))
                {
                    continue;
                }

                try
                {
                    var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);

                    var infrastructureTypes = assembly.GetTypes()
                        .Where(t => t.IsClass && t.IsPublic && !t.IsAbstract && !t.IsGenericType)
                        .Where(HasInfrastructureAttribute)
                        .OrderBy(t => t.FullName)
                        .ToList();

                    foreach (var type in infrastructureTypes)
                    {
                        if (type.FullName != null)
                        {
                            types.Add(type.FullName);
                        }
                    }

                    if (!infrastructureTypes.Any())
                    {
                        var fallbackType = assembly.GetTypes()
                            .Where(t => t.IsClass && t.IsPublic && !t.IsAbstract && !t.IsGenericType)
                            .Where(t => !t.Name.Contains('<') && !t.Name.Contains('>'))
                            .OrderBy(t => t.Name.Length)
                            .ThenBy(t => t.FullName, StringComparer.Ordinal)
                            .FirstOrDefault();

                        if (fallbackType?.FullName != null)
                        {
                            types.Add(fallbackType.FullName);
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (_context.Verbose)
                    {
                        AnsiConsole.MarkupLine($"[dim]  Could not analyze {assemblyName}: {ex.Message}[/]");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (_context.Verbose)
            {
                AnsiConsole.MarkupLine($"[dim]  Error during type analysis: {ex.Message}[/]");
            }
        }

        return types;
    }

    private static bool HasInfrastructureAttribute(Type type)
    {
        try
        {
            return type.CustomAttributes.Any(attr =>
                attr.AttributeType.Name == "InfrastructureAttribute");
        }
        catch
        {
            return false;
        }
    }
}
