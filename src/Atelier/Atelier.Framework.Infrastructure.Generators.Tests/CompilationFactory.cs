using Atelier.Framework.Attributes;
using Atelier.Framework.Outcomes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Atelier.Framework.Infrastructure.Generators.Tests;

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
            MetadataReference.CreateFromFile(typeof(ApiAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Outcome).Assembly.Location),
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
}
