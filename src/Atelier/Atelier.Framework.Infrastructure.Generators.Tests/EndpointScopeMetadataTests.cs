using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Atelier.Framework.Infrastructure.Generators.Tests;

public sealed class EndpointScopeMetadataTests
{
    private const string MutatingSource = """
        using System.Threading.Tasks;
        using Atelier.Framework.Attributes;
        using Atelier.Framework.Outcomes;

        namespace Sample;

        [Api(new[] { "documents.read" })]
        public class DocumentService
        {
            public Task<Outcome<string>> CreateDocumentAsync(string id)
                => Task.FromResult(Outcome<string>.Success(id));
        }
        """;

    private static string Generate(string source)
    {
        var compilation = CompilationFactory.Create(source);
        var generator = new ApiSourceGenerator().AsSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGenerators(compilation);
        var generated = Assert.Single(driver.GetRunResult().GeneratedTrees);
        return generated.GetText().ToString();
    }

    [Fact]
    public void GeneratedEndpoint_CarriesScopeEnforcedOperationMetadata_ForTheServiceBoundaryMiddleware()
    {
        var emitted = Generate(MutatingSource);

        Assert.Contains(".WithMetadata(new global::Atelier.Framework.Network.Middleware.ScopeEnforcedOperation(", emitted);
        Assert.Contains("GetMethod(\"CreateDocumentAsync\")", emitted);
    }
}
