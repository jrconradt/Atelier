using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Atelier.Framework.Infrastructure.Generators.Tests;

public sealed class EndpointAuthGuardHeavyTests
{
    private const string UNAUTH_GUARD = "if (!(context.User?.Identity?.IsAuthenticated ?? false))";
    private const string UNAUTHORIZED_RESULT = "return Results.Unauthorized();";
    private const string FORBIDDEN_RESULT = "return Results.StatusCode(403);";

    private static string SingleClaim(string claim)
    {
        return $$"""
            using System.Threading.Tasks;
            using Atelier.Framework.Attributes;
            using Atelier.Framework.Outcomes;

            namespace Sample;

            [Api(new[] { "{{claim}}" })]
            public class DocumentService
            {
                public Task<Outcome<string>> GetDocumentAsync(string id)
                    => Task.FromResult(Outcome<string>.Success(id));
            }
            """;
    }

    private static string MultiClaim(params string[] claims)
    {
        var literals = string.Join(", ", claims.Select(c => $"\"{c}\""));
        return $$"""
            using System.Threading.Tasks;
            using Atelier.Framework.Attributes;
            using Atelier.Framework.Outcomes;

            namespace Sample;

            [Api(new[] { {{literals}} })]
            public class DocumentService
            {
                public Task<Outcome<string>> GetDocumentAsync(string id)
                    => Task.FromResult(Outcome<string>.Success(id));
            }
            """;
    }

    private const string NO_CLAIMS_SOURCE = """
        using System.Threading.Tasks;
        using Atelier.Framework.Attributes;
        using Atelier.Framework.Outcomes;

        namespace Sample;

        [Api(new string[] { })]
        public class DocumentService
        {
            public Task<Outcome<string>> GetDocumentAsync(string id)
                => Task.FromResult(Outcome<string>.Success(id));
        }
        """;

    private const string ANONYMOUS_METHOD_SOURCE = """
        using System.Threading.Tasks;
        using Atelier.Framework.Attributes;
        using Atelier.Framework.Outcomes;

        namespace Sample;

        [Api(new[] { "documents.read" })]
        public class DocumentService
        {
            [AllowAnonymous]
            public Task<Outcome<string>> GetDocumentAsync(string id)
                => Task.FromResult(Outcome<string>.Success(id));
        }
        """;

    private const string ANONYMOUS_CLASS_SOURCE = """
        using System.Threading.Tasks;
        using Atelier.Framework.Attributes;
        using Atelier.Framework.Outcomes;

        namespace Sample;

        [Api(new[] { "documents.read" })]
        [AllowAnonymous]
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

    private static string HasClaim(string claim)
    {
        return $"!context.User!.HasClaim(\"permission\", \"{claim}\")";
    }

    [Fact]
    public void Unauthenticated_Returns401_NotProceed()
    {
        var emitted = Generate(SingleClaim("documents.read"));

        Assert.Contains(UNAUTH_GUARD, emitted);
        Assert.Contains(UNAUTHORIZED_RESULT, emitted);
    }

    [Fact]
    public void Unauthenticated_GuardPrecedesForbiddenBlock()
    {
        var emitted = Generate(SingleClaim("documents.read"));

        var unauthIndex = emitted.IndexOf(UNAUTH_GUARD, System.StringComparison.Ordinal);
        var forbiddenIndex = emitted.IndexOf(FORBIDDEN_RESULT, System.StringComparison.Ordinal);

        Assert.True(unauthIndex >= 0);
        Assert.True(forbiddenIndex >= 0);
        Assert.True(unauthIndex < forbiddenIndex);
    }

    [Fact]
    public void Unauthenticated_401ReturnSitsBeforeForbiddenReturn()
    {
        var emitted = Generate(SingleClaim("documents.read"));

        var unauthorizedReturn = emitted.IndexOf(UNAUTHORIZED_RESULT, System.StringComparison.Ordinal);
        var forbiddenReturn = emitted.IndexOf(FORBIDDEN_RESULT, System.StringComparison.Ordinal);

        Assert.True(unauthorizedReturn >= 0);
        Assert.True(forbiddenReturn >= 0);
        Assert.True(unauthorizedReturn < forbiddenReturn);
    }

    [Fact]
    public void Authenticated_HasClaim_EmitsExactlyOneClaimCheck()
    {
        var emitted = Generate(SingleClaim("documents.read"));

        Assert.Contains(HasClaim("documents.read"), emitted);
        Assert.Equal(1, CountOccurrences(emitted, HasClaim("documents.read")));
    }

    [Fact]
    public void Authenticated_MissingClaim_Returns403()
    {
        var emitted = Generate(SingleClaim("documents.read"));

        Assert.Contains(HasClaim("documents.read"), emitted);
        Assert.Contains(FORBIDDEN_RESULT, emitted);
    }

    [Fact]
    public void ForbiddenBranch_IsNotGatedByIsAuthenticated()
    {
        var emitted = Generate(SingleClaim("documents.read"));

        Assert.DoesNotContain("?? false) || !context.User!.HasClaim", emitted);
        Assert.DoesNotContain("if (!(context.User?.Identity?.IsAuthenticated ?? false) || ", emitted);
    }

    [Fact]
    public void ForbiddenBranch_ConditionIsPurelyClaimNegations()
    {
        var emitted = Generate(MultiClaim("documents.read", "documents.write"));

        Assert.DoesNotContain("IsAuthenticated ?? false) ||", emitted);
        Assert.DoesNotContain("IsAuthenticated ?? false) &&", emitted);
    }

    [Fact]
    public void SingleClaim_ForbiddenBlockHasNoOrConnector()
    {
        var emitted = Generate(SingleClaim("documents.read"));

        Assert.DoesNotContain("HasClaim(\"permission\", \"documents.read\")\n            ||", emitted);
    }

    [Fact]
    public void DuplicateClaims_CollapseToSingleCheck()
    {
        var emitted = Generate(MultiClaim("documents.read", "documents.read"));

        Assert.Equal(1, CountOccurrences(emitted, HasClaim("documents.read")));
    }

    [Fact]
    public void TripleDuplicateClaims_CollapseToSingleCheck()
    {
        var emitted = Generate(MultiClaim("documents.read", "documents.read", "documents.read"));

        Assert.Equal(1, CountOccurrences(emitted, HasClaim("documents.read")));
    }

    [Fact]
    public void DuplicateAmongDistinct_DedupesOnlyDuplicate()
    {
        var emitted = Generate(MultiClaim("documents.read", "documents.write", "documents.read"));

        Assert.Equal(1, CountOccurrences(emitted, HasClaim("documents.read")));
        Assert.Equal(1, CountOccurrences(emitted, HasClaim("documents.write")));
    }

    [Fact]
    public void TwoClaims_BothRendered()
    {
        var emitted = Generate(MultiClaim("documents.read", "documents.write"));

        Assert.Contains(HasClaim("documents.read"), emitted);
        Assert.Contains(HasClaim("documents.write"), emitted);
    }

    [Fact]
    public void TwoClaims_JoinedByOrConnector()
    {
        var emitted = Generate(MultiClaim("documents.read", "documents.write"));

        Assert.Contains("||", emitted);
        Assert.Contains(HasClaim("documents.read") + "\n                                || " + HasClaim("documents.write"), emitted);
    }

    [Fact]
    public void ManyClaims_AllRendered()
    {
        var emitted = Generate(MultiClaim("a.read", "b.read", "c.read", "d.write", "e.delete"));

        Assert.Contains(HasClaim("a.read"), emitted);
        Assert.Contains(HasClaim("b.read"), emitted);
        Assert.Contains(HasClaim("c.read"), emitted);
        Assert.Contains(HasClaim("d.write"), emitted);
        Assert.Contains(HasClaim("e.delete"), emitted);
    }

    [Fact]
    public void ManyClaims_JoinedByOrSoMissingAnyForbids()
    {
        var emitted = Generate(MultiClaim("a.read", "b.read", "c.read", "d.write", "e.delete"));

        var orCount = CountOccurrences(emitted, "\n                                || !context.User!.HasClaim");
        Assert.Equal(4, orCount);
    }

    [Fact]
    public void ManyClaims_NoAndConnectorInClaimChain()
    {
        var emitted = Generate(MultiClaim("a.read", "b.read", "c.read"));

        Assert.DoesNotContain("HasClaim(\"permission\", \"a.read\")\n            && ", emitted);
    }

    [Fact]
    public void ZeroClaims_EmitsAuthRequirementOnly()
    {
        var emitted = Generate(NO_CLAIMS_SOURCE);

        Assert.Contains(UNAUTH_GUARD, emitted);
        Assert.Contains(UNAUTHORIZED_RESULT, emitted);
    }

    [Fact]
    public void ZeroClaims_EmitsNoForbiddenBlock()
    {
        var emitted = Generate(NO_CLAIMS_SOURCE);

        Assert.DoesNotContain(FORBIDDEN_RESULT, emitted);
        Assert.DoesNotContain("HasClaim", emitted);
    }

    [Fact]
    public void AllowAnonymousMethod_EmitsNoGuard()
    {
        var emitted = Generate(ANONYMOUS_METHOD_SOURCE);

        Assert.DoesNotContain(UNAUTH_GUARD, emitted);
        Assert.DoesNotContain(UNAUTHORIZED_RESULT, emitted);
        Assert.DoesNotContain("HasClaim", emitted);
    }

    [Fact]
    public void AllowAnonymousClass_EmitsNoGuard()
    {
        var emitted = Generate(ANONYMOUS_CLASS_SOURCE);

        Assert.DoesNotContain(UNAUTH_GUARD, emitted);
        Assert.DoesNotContain(UNAUTHORIZED_RESULT, emitted);
        Assert.DoesNotContain("HasClaim", emitted);
    }

    [Fact]
    public void ClaimType_IsAlwaysPermission()
    {
        var emitted = Generate(SingleClaim("documents.read"));

        Assert.Contains("HasClaim(\"permission\",", emitted);
    }

    [Fact]
    public void ClaimWithDots_RenderedVerbatim()
    {
        var emitted = Generate(SingleClaim("atelier.boutique.read"));

        Assert.Contains(HasClaim("atelier.boutique.read"), emitted);
    }

    [Fact]
    public void ForbiddenResult_IsStatusCode403_Not401()
    {
        var emitted = Generate(SingleClaim("documents.read"));

        Assert.Contains("Results.StatusCode(403)", emitted);
        Assert.DoesNotContain("Results.StatusCode(401)", emitted);
    }

    [Fact]
    public void RequiredClaimsList_RenderedInAuditValues()
    {
        var emitted = Generate(MultiClaim("documents.read", "documents.write"));

        Assert.Contains("(\"RequiredClaims\", \"documents.read, documents.write\")", emitted);
    }

    [Fact]
    public void Adversarial_AuthenticatedLackingEveryClaim_StillForbidden()
    {
        var emitted = Generate(MultiClaim("a.read", "b.write"));

        var forbiddenIndex = emitted.IndexOf(FORBIDDEN_RESULT, System.StringComparison.Ordinal);
        Assert.True(forbiddenIndex >= 0);

        Assert.Contains(HasClaim("a.read") + "\n                                || " + HasClaim("b.write"), emitted);
    }

    [Fact]
    public void Adversarial_NoAdmitPathForClaimlessPrincipal()
    {
        var emitted = Generate(SingleClaim("documents.read"));

        Assert.Contains("if (" + HasClaim("documents.read") + ")", emitted);
        Assert.DoesNotContain("if (context.User!.HasClaim(\"permission\", \"documents.read\"))\n        {\n            return Results.Unauthorized", emitted);
    }

    [Fact]
    public void Adversarial_ForbiddenConditionUsesNegatedHasClaim()
    {
        var emitted = Generate(SingleClaim("documents.read"));

        Assert.Contains("if (!context.User!.HasClaim(\"permission\", \"documents.read\"))", emitted);
        Assert.DoesNotContain("if (context.User!.HasClaim(\"permission\", \"documents.read\"))", emitted);
    }

    [Fact]
    public void Adversarial_GuardBlockOrderUnauthenticatedThenForbidden()
    {
        var emitted = Generate(SingleClaim("documents.read"));

        var auth = emitted.IndexOf(UNAUTH_GUARD, System.StringComparison.Ordinal);
        var claim = emitted.IndexOf(HasClaim("documents.read"), System.StringComparison.Ordinal);

        Assert.True(auth >= 0);
        Assert.True(claim >= 0);
        Assert.True(auth < claim);
    }

    [Fact]
    public void Adversarial_EachClaimNegationIndependent_NoConjunctionWeakening()
    {
        var emitted = Generate(MultiClaim("a.read", "b.read", "c.read"));

        Assert.DoesNotContain(" && !context.User!.HasClaim", emitted);
        Assert.Equal(2, CountOccurrences(emitted, "\n                                || !context.User!.HasClaim"));
    }

    [Fact]
    public void Adversarial_DedupDoesNotDropDistinctClaims()
    {
        var emitted = Generate(MultiClaim("x.read", "x.read", "y.write", "y.write", "z.delete"));

        Assert.Equal(1, CountOccurrences(emitted, HasClaim("x.read")));
        Assert.Equal(1, CountOccurrences(emitted, HasClaim("y.write")));
        Assert.Equal(1, CountOccurrences(emitted, HasClaim("z.delete")));
        Assert.Equal(2, CountOccurrences(emitted, "\n                                || !context.User!.HasClaim"));
    }

    [Fact]
    public void Adversarial_Unauthorized_NeverFusedWithClaimCheckIntoSingleCondition()
    {
        var emitted = Generate(MultiClaim("a.read", "b.read"));

        Assert.DoesNotContain("IsAuthenticated ?? false) || !context.User!.HasClaim", emitted);
    }

    [Fact]
    public void SingleClaim_ExactGuardSequence()
    {
        var emitted = Generate(SingleClaim("documents.read"));

        var unauth = emitted.IndexOf(UNAUTHORIZED_RESULT, System.StringComparison.Ordinal);
        var claim = emitted.IndexOf("if (" + HasClaim("documents.read") + ")", System.StringComparison.Ordinal);
        var forbidden = emitted.IndexOf(FORBIDDEN_RESULT, System.StringComparison.Ordinal);

        Assert.True(unauth >= 0 && claim >= 0 && forbidden >= 0);
        Assert.True(unauth < claim);
        Assert.True(claim < forbidden);
    }

    [Fact]
    public void TwoMethods_NotApplicable_SingleMethodFixtureOnly()
    {
        var emitted = Generate(SingleClaim("documents.read"));

        Assert.Equal(1, CountOccurrences(emitted, UNAUTH_GUARD));
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
