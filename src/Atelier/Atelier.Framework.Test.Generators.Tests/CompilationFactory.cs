using Atelier.Framework.Attributes;
using Atelier.Framework.Observability;
using Atelier.Framework.Offering;
using Atelier.Framework.Offering.Product;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;

namespace Atelier.Framework.Test.Generators.Tests;

internal static class CompilationFactory
{
    private static readonly string[] RuntimeAssemblyNames =
    {
        "System.Runtime",
        "System.Collections",
        "System.Linq",
        "System.Threading.Tasks",
        "netstandard",
    };

    public static CSharpCompilation Create(string source, string assemblyName = "FixtureAssembly")
    {
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(OperationAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(RequisiteAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IAtelier).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Outcome).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ProductBase).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IOffering).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location),
        };

        var trustedPlatform = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
        foreach (var path in trustedPlatform.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (RuntimeAssemblyNames.Contains(name))
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
        }

        var tree = CSharpSyntaxTree.ParseText(source);
        return CSharpCompilation.Create(
            assemblyName,
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    public static INamedTypeSymbol GetType(CSharpCompilation compilation, string metadataName)
    {
        var symbol = compilation.GetTypeByMetadataName(metadataName);
        if (symbol is null)
        {
            throw new InvalidOperationException($"Type '{metadataName}' was not found in the fixture compilation.");
        }
        return symbol;
    }
}
