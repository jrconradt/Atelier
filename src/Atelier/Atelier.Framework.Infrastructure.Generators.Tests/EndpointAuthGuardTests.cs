using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Atelier.Framework.Infrastructure.Generators.Tests;

public sealed class EndpointAuthGuardTests
{
    private const string SingleClaimSource = """
        using System.Threading.Tasks;
        using Atelier.Framework.Attributes;
        using Atelier.Framework.Outcomes;

        namespace Sample;

        [Api(new[] { "documents.read" })]
        public class DocumentService
        {
            public Task<Outcome<string>> GetDocumentAsync(string id)
                => Task.FromResult(Outcome<string>.Success(id));
        }
        """;

    private const string DuplicateClaimSource = """
        using System.Threading.Tasks;
        using Atelier.Framework.Attributes;
        using Atelier.Framework.Outcomes;

        namespace Sample;

        [Api(new[] { "documents.read", "documents.read" })]
        public class DocumentService
        {
            public Task<Outcome<string>> GetDocumentAsync(string id)
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
    public void UnauthenticatedCaller_IsRejectedWith401()
    {
        var emitted = Generate(SingleClaimSource);

        Assert.Contains("if (!(context.User?.Identity?.IsAuthenticated ?? false))", emitted);
        Assert.Contains("return Results.Unauthorized();", emitted);
    }

    [Fact]
    public void AuthenticatedCallerMissingClaim_IsRejectedWith403()
    {
        var emitted = Generate(SingleClaimSource);

        Assert.Contains("if (!context.User!.HasClaim(\"permission\", \"documents.read\"))", emitted);
        Assert.Contains("return Results.StatusCode(403);", emitted);
    }

    [Fact]
    public void ForbiddenBranch_IsNotGatedByIsAuthenticated()
    {
        var emitted = Generate(SingleClaimSource);

        Assert.DoesNotContain("?? false) || !context.User!.HasClaim", emitted);
        Assert.DoesNotContain("if (!(context.User?.Identity?.IsAuthenticated ?? false) || ", emitted);
    }

    [Fact]
    public void DuplicateClaims_CollapseToSingleCheck()
    {
        var emitted = Generate(DuplicateClaimSource);

        var occurrences = CountOccurrences(emitted, "HasClaim(\"permission\", \"documents.read\")");
        Assert.Equal(1, occurrences);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while (true)
        {
            var found = haystack.IndexOf(needle, index, System.StringComparison.Ordinal);
            if (found < 0)
            {
                break;
            }
            count++;
            index = found + needle.Length;
        }
        return count;
    }
}
