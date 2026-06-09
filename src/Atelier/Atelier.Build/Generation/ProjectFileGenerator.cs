using System.Collections.Concurrent;
using System.Xml.Linq;
using Atelier.Build.Analysis;
using Atelier.Build.Discovery;
using Atelier.Build.Pipeline;
using Spectre.Console;

namespace Atelier.Build.Generation;

public class ProjectFileGenerator
{
    private readonly BuildContext _context;

    private static readonly ConcurrentDictionary<string, Dictionary<string, string>> _csprojIndexByRoot = new(StringComparer.OrdinalIgnoreCase);

    public ProjectFileGenerator(BuildContext context)
    {
        _context = context;
    }

    private static Dictionary<string, string> GetCsprojIndex(string srcDirectory)
    {
        return _csprojIndexByRoot.GetOrAdd(srcDirectory, root =>
        {
            var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (Directory.Exists(root))
            {
                foreach (var csproj in Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories))
                {
                    if (IsExcludedProjectPath(csproj))
                    {
                        continue;
                    }

                    var name = Path.GetFileNameWithoutExtension(csproj);
                    if (!index.ContainsKey(name))
                    {
                        index[name] = csproj;
                    }
                }
            }

            return index;
        });
    }

    public async Task<string> GenerateAsync(
        BoutiqueYamlSchema schema,
        ProductDependencyGraph dependencyGraph,
        string outputDirectory,
        string solutionRoot)
    {
        var boutiqueName = schema.SubsystemName
            ?? Atelier.Build.Utils.Naming.ToBoutiqueAssemblyIdentifier(schema.Name);
        var assemblyName = $"Atelier.Host.{boutiqueName}";

        var projectXml = GenerateProjectXml(schema,
                                            dependencyGraph,
                                            assemblyName,
                                            solutionRoot,
                                            outputDirectory);

        var outputPath = Path.Combine(outputDirectory, $"{assemblyName}.csproj");
        await File.WriteAllTextAsync(outputPath, projectXml).ConfigureAwait(false);

        if (_context.Verbose)
        {
            AnsiConsole.MarkupLine($"[dim]    → Generated {assemblyName}.csproj[/]");
        }

        return outputPath;
    }

    private string GenerateProjectXml(
        BoutiqueYamlSchema schema,
        ProductDependencyGraph dependencyGraph,
        string assemblyName,
        string solutionRoot,
        string outputDirectory)
    {
        var boutiqueName = schema.SubsystemName
            ?? Atelier.Build.Utils.Naming.ToBoutiqueAssemblyIdentifier(schema.Name);

        var projectChildren = new List<object>
        {
            new XAttribute("Sdk", "Microsoft.NET.Sdk.Web"),
            CreatePropertyGroup(schema,
                                assemblyName,
                                boutiqueName),
            CreateSelfContainedPropertyGroup(),
            CreateTrimmingPropertyGroup(schema),
            CreateProjectReferences(schema,
                                    dependencyGraph,
                                    solutionRoot,
                                    outputDirectory),
            CreatePackageReferences(schema)
        };

        var protobufGroup = CreateProtobufItemGroup(schema);
        if (protobufGroup != null)
        {
            projectChildren.Add(protobufGroup);
        }

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("Project", projectChildren.ToArray())
        );

        return doc.ToString();
    }

    private XElement CreatePropertyGroup(BoutiqueYamlSchema schema,
                                         string assemblyName,
                                         string boutiqueName)
    {
        const string FRAMEWORK = "net10.0";

        var elements = new List<XElement>
        {
            new XElement("TargetFramework", FRAMEWORK),
            new XElement("OutputType", "Exe"),
            new XElement("AssemblyName", assemblyName),
            new XElement("RootNamespace", $"Atelier.Host.{boutiqueName}"),
            new XElement("ImplicitUsings", "enable"),
            new XElement("Nullable", "enable"),
            new XElement("TreatWarningsAsErrors", schema.Build?.TreatWarningsAsErrors ?? false ? "true" : "false"),
            new XElement("GenerateDocumentationFile", "false"),
            new XElement("NoWarn", "CS1591")
        };

        if (schema.Build?.AllowUnsafeBlocks == true)
        {
            elements.Add(new XElement("AllowUnsafeBlocks", "true"));
        }

        return new XElement("PropertyGroup", elements.ToArray());
    }

    private XElement CreateSelfContainedPropertyGroup()
    {
        return new XElement("PropertyGroup",
            new XAttribute("Condition", "'$(SelfContained)' == 'true'"),
            new XElement("PublishSingleFile", "true"),
            new XElement("EnableCompressionInSingleFile", "true"),
            new XElement("IncludeNativeLibrariesForSelfExtract", "true")
        );
    }

    private XElement CreateTrimmingPropertyGroup(BoutiqueYamlSchema schema)
    {
        return new XElement("PropertyGroup",
            new XAttribute("Condition", "'$(PublishTrimmed)' == 'true'"),
            new XElement("TrimMode", "link"),
            new XElement("TrimmerRemoveSymbols", "true")
        );
    }

    private XElement CreateProjectReferences(
        BoutiqueYamlSchema schema,
        ProductDependencyGraph dependencyGraph,
        string solutionRoot,
        string outputDirectory)
    {
        var itemGroup = new XElement("ItemGroup");

        var addedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var coreAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Atelier.Framework.Infrastructure",
            "Atelier.Framework.Context",
            "Atelier.Framework.Observability",
            "Atelier.Framework.Outcomes",
            "Atelier.Framework.Requisitions",
            "Atelier.Framework.Offering",
            "Atelier.Framework.Attache",
            "Atelier.Framework.Facility",
            "Atelier.Framework.EventStream"
        };

        if (schema.Infrastructure?.Network?.Enabled == true)
        {
            coreAssemblies.Add("Atelier.Framework.Network");
        }

        foreach (var assembly in coreAssemblies)
        {
            var relativePath = FindProjectPath(assembly,
                                               solutionRoot,
                                               outputDirectory);
            if (relativePath != null)
            {
                AddProjectReference(itemGroup, addedPaths, relativePath);
            }
        }

        var boutiqueSpecificAssemblies = dependencyGraph.GetBoutiqueSpecificAssemblies().ToList();
        if (_context.Verbose && boutiqueSpecificAssemblies.Any())
        {
            AnsiConsole.MarkupLine($"[blue]  dependencyGraph has {boutiqueSpecificAssemblies.Count} assemblies[/]");
        }

        foreach (var assembly in boutiqueSpecificAssemblies)
        {
            if (coreAssemblies.Contains(assembly))
            {
                continue;
            }

            var relativePath = FindProjectPath(assembly,
                                               solutionRoot,
                                               outputDirectory);
            if (relativePath != null)
            {
                AddProjectReference(itemGroup, addedPaths, relativePath);
            }
        }

        if (schema.ProjectReferences != null && schema.ProjectReferences.Count > 0)
        {
            if (_context.Verbose)
            {
                AnsiConsole.MarkupLine($"[cyan]  schema.ProjectReferences has {schema.ProjectReferences.Count} items:[/]");
            }

            foreach (var projectRef in schema.ProjectReferences)
            {
                if (_context.Verbose)
                {
                    AnsiConsole.MarkupLine($"[dim]    Ref: {projectRef}[/]");
                }

                string? relativePath;

                if (Path.IsPathRooted(projectRef) && File.Exists(projectRef))
                {
                    relativePath = GetRelativePath(outputDirectory, projectRef);
                }
                else
                {
                    relativePath = FindProjectPath(projectRef,
                                                   solutionRoot,
                                                   outputDirectory);
                }

                if (relativePath != null)
                {
                    AddProjectReference(itemGroup, addedPaths, relativePath);
                }
                else if (_context.Verbose)
                {
                    AnsiConsole.MarkupLine($"[red]    → Could not resolve: {projectRef}[/]");
                }
            }
        }

        return itemGroup;
    }

        private static void AddProjectReference(
        XElement itemGroup,
        HashSet<string> addedPaths,
        string relativePath)
    {
        if (!addedPaths.Add(relativePath))
        {
            return;
        }

        var fileName = Path.GetFileNameWithoutExtension(relativePath);
        var isGenerator = fileName.EndsWith(".Generators", StringComparison.OrdinalIgnoreCase);

        var element = new XElement("ProjectReference",
            new XAttribute("Include", relativePath));

        if (isGenerator)
        {
            element.Add(new XAttribute("OutputItemType", "Analyzer"));
            element.Add(new XAttribute("ReferenceOutputAssembly", "false"));
        }

        itemGroup.Add(element);
    }

    private string? FindProjectPath(string assemblyName,
                                    string solutionRoot,
                                    string outputDirectory)
    {
        var possiblePaths = new[]
        {
            Path.Combine(solutionRoot, assemblyName, $"{assemblyName}.csproj"),
            Path.Combine(solutionRoot, assemblyName.Replace(".", Path.DirectorySeparatorChar.ToString()), $"{assemblyName}.csproj")
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                return GetRelativePath(outputDirectory, path);
            }
        }

        var index = GetCsprojIndex(solutionRoot);
        if (index.TryGetValue(assemblyName, out var found))
        {
            return GetRelativePath(outputDirectory, found);
        }

        if (_context.Verbose)
        {
            AnsiConsole.MarkupLine($"[dim]    Could not find project for {assemblyName}[/]");
        }

        return null;
    }

    private static bool IsExcludedProjectPath(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var segment in segments)
        {
            if (segment.Equals("bin", StringComparison.Ordinal)
                || segment.Equals("obj", StringComparison.Ordinal)
                || segment.Equals(".artifacts", StringComparison.Ordinal)
                || segment.Equals("boutiques", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetRelativePath(string fromDirectory, string toFile)
    {
        var fromUri = new Uri(fromDirectory.EndsWith(Path.DirectorySeparatorChar.ToString())
            ? fromDirectory
            : fromDirectory + Path.DirectorySeparatorChar);
        var toUri = new Uri(toFile);

        var relativeUri = fromUri.MakeRelativeUri(toUri);
        var relativePath = Uri.UnescapeDataString(relativeUri.ToString());

        return relativePath.Replace('/', Path.DirectorySeparatorChar);
    }

    private XElement CreatePackageReferences(BoutiqueYamlSchema schema)
    {
        var itemGroup = new XElement("ItemGroup");

        itemGroup.Add(new XElement("PackageReference",
            new XAttribute("Include", "Microsoft.AspNetCore.OpenApi")));



        if (schema.Capabilities?.Grpc?.Enabled == true)
        {
            itemGroup.Add(new XElement("PackageReference",
                new XAttribute("Include", "Grpc.AspNetCore")));
        }

        return itemGroup;
    }

    private XElement? CreateProtobufItemGroup(BoutiqueYamlSchema schema)
    {
        if (schema.Build?.Protos == null || schema.Build.Protos.Count == 0)
        {
            return null;
        }

        var itemGroup = new XElement("ItemGroup");
        foreach (var protoFile in schema.Build.Protos)
        {
            itemGroup.Add(new XElement("Protobuf",
                new XAttribute("Include", protoFile),
                new XAttribute("GrpcServices", "Server")));
        }

        return itemGroup;
    }
}
