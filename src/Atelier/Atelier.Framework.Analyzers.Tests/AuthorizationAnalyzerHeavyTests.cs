using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Atelier.Framework.Analyzers.Tests;

public sealed class AuthorizationAnalyzerHeavyTests
{
    private const string SCOPE_CATALOG = "Atelier.Framework.Identity.Authorization.Scopes";
    private const string CLAIM_CATALOG = "Atelier.Framework.Identity.Authorization.Claims";

    private static DiagnosticResult Scope0741(int startLine,
                                              int startCol,
                                              int endLine,
                                              int endCol,
                                              string literal)
    {
        return new DiagnosticResult("ATELIER0741", DiagnosticSeverity.Error)
            .WithSpan(startLine, startCol, endLine, endCol)
            .WithArguments(literal, SCOPE_CATALOG);
    }

    private static DiagnosticResult Claim0741(int startLine,
                                              int startCol,
                                              int endLine,
                                              int endCol,
                                              string literal)
    {
        return new DiagnosticResult("ATELIER0741", DiagnosticSeverity.Error)
            .WithSpan(startLine, startCol, endLine, endCol)
            .WithArguments(literal, CLAIM_CATALOG);
    }

    private static DiagnosticResult Mutating0750(int startLine,
                                                 int startCol,
                                                 int endLine,
                                                 int endCol,
                                                 string method,
                                                 string type)
    {
        return new DiagnosticResult("ATELIER0750", DiagnosticSeverity.Error)
            .WithSpan(startLine, startCol, endLine, endCol)
            .WithArguments(method, type);
    }

    private static string ApiOperation(string methodName,
                                       string effect)
    {
        return $$"""
            using System.Threading.Tasks;
            using Atelier.Framework.Attributes;

            namespace Sample;

            [Api(null)]
            public sealed class BoutiqueService
            {
                [OperationEffect(EffectKind.{{effect}})]
                public Task {{methodName}}()
                {
                    return Task.CompletedTask;
                }
            }
            """;
    }

    [Theory]
    [InlineData("CreateBoutiqueAsync")]
    [InlineData("UpdateBoutiqueAsync")]
    [InlineData("DeleteBoutiqueAsync")]
    [InlineData("PatchBoutiqueAsync")]
    [InlineData("PublishBoutiqueAsync")]
    [InlineData("ReplaceBoutiqueAsync")]
    [InlineData("ApproveBoutiqueAsync")]
    [InlineData("ReticulateBoutiqueAsync")]
    [InlineData("FrobnicateAsync")]
    [InlineData("SaveAsync")]
    public async Task Atelier0750_FiresForEachWriteEffectOperation(string methodName)
    {
        var source = ApiOperation(methodName, "Write");
        var endCol = 17 + methodName.Length;
        var expected = Mutating0750(10, 17, 10, endCol, methodName, "BoutiqueService");
        await AnalyzerVerify.FiresAsync<MutatingApiScopeAnalyzer>(source, expected);
    }

    [Theory]
    [InlineData("GetBoutiqueAsync")]
    [InlineData("FetchBoutiqueAsync")]
    [InlineData("RetrieveBoutiqueAsync")]
    [InlineData("DiscoverBoutiquesAsync")]
    [InlineData("FindBoutiqueAsync")]
    [InlineData("ListBoutiquesAsync")]
    [InlineData("QueryBoutiquesAsync")]
    [InlineData("SearchBoutiquesAsync")]
    public async Task Atelier0750_SilentForEachReadEffectOperation(string methodName)
    {
        var source = ApiOperation(methodName, "Read");
        await AnalyzerVerify.SilentAsync<MutatingApiScopeAnalyzer>(source);
    }

    [Fact]
    public async Task Atelier0750_FiresForMultipleWriteEffectOperationsOnOneClass()
    {
        const string source = """
            using System.Threading.Tasks;
            using Atelier.Framework.Attributes;

            namespace Sample;

            [Api(null)]
            public sealed class BoutiqueService
            {
                [OperationEffect(EffectKind.Write)]
                public Task CreateBoutiqueAsync()
                {
                    return Task.CompletedTask;
                }

                [OperationEffect(EffectKind.Write)]
                public Task DeleteBoutiqueAsync()
                {
                    return Task.CompletedTask;
                }

                [OperationEffect(EffectKind.Read)]
                public Task GetBoutiqueAsync()
                {
                    return Task.CompletedTask;
                }
            }
            """;

        var create = Mutating0750(10, 17, 10, 36, "CreateBoutiqueAsync", "BoutiqueService");
        var delete = Mutating0750(16, 17, 16, 36, "DeleteBoutiqueAsync", "BoutiqueService");
        await AnalyzerVerify.FiresAsync<MutatingApiScopeAnalyzer>(source, create, delete);
    }

    [Fact]
    public async Task Atelier0750_SilentWhenScopeResourceOnImplementedInterface()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Atelier.Framework.Attributes;

            namespace Sample;

            public static class BoutiqueScopes
            {
                public const string READ = "atelier.boutique.read";
                public const string WRITE = "atelier.boutique.write";
            }

            [ScopeResource(typeof(BoutiqueScopes))]
            public interface IBoutiqueService
            {
                Task CreateBoutiqueAsync();
            }

            [Api(null)]
            public sealed class BoutiqueService : IBoutiqueService
            {
                [OperationEffect(EffectKind.Write)]
                public Task CreateBoutiqueAsync()
                {
                    return Task.CompletedTask;
                }
            }
            """;

        await AnalyzerVerify.SilentAsync<MutatingApiScopeAnalyzer>(source);
    }

    [Fact]
    public async Task Atelier0750_SilentWhenMethodHasRequiresScopeContract()
    {
        const string source = """
            using System.Threading.Tasks;
            using Atelier.Framework.Attributes;
            using Atelier.Framework.Identity.Authorization;

            namespace Atelier.Framework.Identity.Authorization
            {
                public static class Scopes
                {
                    public static class Boutique
                    {
                        public const string WRITE = "atelier.boutique.write";
                    }
                }
            }

            namespace Sample
            {
                [Api(null)]
                [RequiresScopeContract(Scopes.Boutique.WRITE)]
                public sealed class BoutiqueService
                {
                    [OperationEffect(EffectKind.Write)]
                    public System.Threading.Tasks.Task CreateBoutiqueAsync()
                    {
                        return System.Threading.Tasks.Task.CompletedTask;
                    }
                }
            }
            """;

        await AnalyzerVerify.SilentAsync<MutatingApiScopeAnalyzer>(source);
    }

    [Fact]
    public async Task Atelier0750_SilentWhenMethodPrivate()
    {
        const string source = """
            using System.Threading.Tasks;
            using Atelier.Framework.Attributes;

            namespace Sample;

            [Api(null)]
            public sealed class BoutiqueService
            {
                [OperationEffect(EffectKind.Write)]
                private Task CreateBoutiqueAsync()
                {
                    return Task.CompletedTask;
                }
            }
            """;

        await AnalyzerVerify.SilentAsync<MutatingApiScopeAnalyzer>(source);
    }

    [Fact]
    public async Task Atelier0750_SilentWhenMethodStatic()
    {
        const string source = """
            using System.Threading.Tasks;
            using Atelier.Framework.Attributes;

            namespace Sample;

            [Api(null)]
            public sealed class BoutiqueService
            {
                [OperationEffect(EffectKind.Write)]
                public static Task CreateBoutiqueAsync()
                {
                    return Task.CompletedTask;
                }
            }
            """;

        await AnalyzerVerify.SilentAsync<MutatingApiScopeAnalyzer>(source);
    }

    [Fact]
    public async Task Atelier0750_FiresOnWriteEffectVoidMethod()
    {
        const string source = """
            using Atelier.Framework.Attributes;

            namespace Sample;

            [Api(null)]
            public sealed class BoutiqueService
            {
                [OperationEffect(EffectKind.Write)]
                public void CreateBoutique()
                {
                }
            }
            """;

        var expected = Mutating0750(9, 17, 9, 31, "CreateBoutique", "BoutiqueService");
        await AnalyzerVerify.FiresAsync<MutatingApiScopeAnalyzer>(source, expected);
    }

    [Fact]
    public async Task Atelier0750_SilentWhenClassHasNoApiAttribute()
    {
        const string source = """
            using System.Threading.Tasks;
            using Atelier.Framework.Attributes;

            namespace Sample;

            public sealed class BoutiqueService
            {
                [OperationEffect(EffectKind.Write)]
                public Task CreateBoutiqueAsync()
                {
                    return Task.CompletedTask;
                }
            }
            """;

        await AnalyzerVerify.SilentAsync<MutatingApiScopeAnalyzer>(source);
    }

    [Fact]
    public async Task Atelier0750_SilentWhenClassMarkedAnonymous()
    {
        const string source = """
            using System.Threading.Tasks;
            using Atelier.Framework.Attributes;

            namespace Sample;

            [Api(null)]
            [AllowAnonymous]
            public sealed class BoutiqueService
            {
                [OperationEffect(EffectKind.Write)]
                public Task CreateBoutiqueAsync()
                {
                    return Task.CompletedTask;
                }
            }
            """;

        await AnalyzerVerify.SilentAsync<MutatingApiScopeAnalyzer>(source);
    }

    [Fact]
    public async Task Atelier0750_FiresWhenUnrelatedAttributePresentInsteadOfScope()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Atelier.Framework.Attributes;

            namespace Sample;

            [AttributeUsage(AttributeTargets.Method)]
            public sealed class AuditedAttribute : Attribute
            {
            }

            [Api(null)]
            public sealed class BoutiqueService
            {
                [Audited]
                [OperationEffect(EffectKind.Write)]
                public Task CreateBoutiqueAsync()
                {
                    return Task.CompletedTask;
                }
            }
            """;

        var expected = Mutating0750(17, 17, 17, 36, "CreateBoutiqueAsync", "BoutiqueService");
        await AnalyzerVerify.FiresAsync<MutatingApiScopeAnalyzer>(source, expected);
    }

    [Fact]
    public async Task Atelier0750_FiresWhenScopeResourceNamedAttributeIsImpostorFromDifferentSimpleName()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Atelier.Framework.Attributes;

            namespace Sample;

            [AttributeUsage(AttributeTargets.Class)]
            public sealed class ScopeResourceMarkerAttribute : Attribute
            {
            }

            [Api(null)]
            [ScopeResourceMarker]
            public sealed class BoutiqueService
            {
                [OperationEffect(EffectKind.Write)]
                public Task CreateBoutiqueAsync()
                {
                    return Task.CompletedTask;
                }
            }
            """;

        var expected = Mutating0750(17, 17, 17, 36, "CreateBoutiqueAsync", "BoutiqueService");
        await AnalyzerVerify.FiresAsync<MutatingApiScopeAnalyzer>(source, expected);
    }

    [Fact]
    public async Task Atelier0750_FiresWhenMethodReturnsValueTask()
    {
        const string source = """
            using System.Threading.Tasks;
            using Atelier.Framework.Attributes;

            namespace Sample;

            [Api(null)]
            public sealed class BoutiqueService
            {
                [OperationEffect(EffectKind.Write)]
                public ValueTask CreateBoutiqueAsync()
                {
                    return ValueTask.CompletedTask;
                }
            }
            """;

        var expected = Mutating0750(10, 22, 10, 41, "CreateBoutiqueAsync", "BoutiqueService");
        await AnalyzerVerify.FiresAsync<MutatingApiScopeAnalyzer>(source, expected);
    }

    [Fact]
    public async Task Atelier0750_FiresOnWriteEffectMethodReturningCustomAwaitable()
    {
        const string source = """
            using Atelier.Framework.Attributes;

            namespace Sample;

            public readonly struct Promise
            {
            }

            [Api(null)]
            public sealed class BoutiqueService
            {
                [OperationEffect(EffectKind.Write)]
                public Promise CreateBoutiqueAsync()
                {
                    return default;
                }
            }
            """;

        var expected = Mutating0750(13, 20, 13, 39, "CreateBoutiqueAsync", "BoutiqueService");
        await AnalyzerVerify.FiresAsync<MutatingApiScopeAnalyzer>(source, expected);
    }

    [Fact]
    public async Task Atelier0741_FiresOnRawClaimLiteral()
    {
        const string source = """
            using Atelier.Framework.Attributes;

            namespace Atelier.Framework.Identity.Authorization
            {
                public static class Claims
                {
                    public const string BOUTIQUE_READ = "atelier.boutique.read";
                }
            }

            namespace Sample
            {
                public sealed class Reader
                {
                    [RequiresClaim("atelier.boutique.read")]
                    public string Read()
                    {
                        return "x";
                    }
                }
            }
            """;

        var expected = Claim0741(15, 24, 15, 47, "\"atelier.boutique.read\"");
        await AnalyzerVerify.FiresAsync<ClosedSetAuthorizationLiteralAnalyzer>(source, expected);
    }

    [Fact]
    public async Task Atelier0741_FiresOnRawClaimValueNamedArgument()
    {
        const string source = """
            using Atelier.Framework.Attributes;

            namespace Atelier.Framework.Identity.Authorization
            {
                public static class Claims
                {
                    public const string BOUTIQUE_READ = "atelier.boutique.read";
                }
            }

            namespace Sample
            {
                public sealed class Reader
                {
                    [RequiresClaim(Atelier.Framework.Identity.Authorization.Claims.BOUTIQUE_READ, ClaimValue = "raw-value")]
                    public string Read()
                    {
                        return "x";
                    }
                }
            }
            """;

        var expected = Claim0741(15, 100, 15, 111, "\"raw-value\"");
        await AnalyzerVerify.FiresAsync<ClosedSetAuthorizationLiteralAnalyzer>(source, expected);
    }

    [Fact]
    public async Task Atelier0741_SilentOnFlatCatalogClaimConstant()
    {
        const string source = """
            using Atelier.Framework.Attributes;
            using Atelier.Framework.Identity.Authorization;

            namespace Atelier.Framework.Identity.Authorization
            {
                public static class Claims
                {
                    public const string BOUTIQUE_READ = "atelier.boutique.read";
                }
            }

            namespace Sample
            {
                public sealed class Reader
                {
                    [RequiresClaim(Claims.BOUTIQUE_READ)]
                    public string Read()
                    {
                        return "x";
                    }
                }
            }
            """;

        await AnalyzerVerify.SilentAsync<ClosedSetAuthorizationLiteralAnalyzer>(source);
    }

    [Fact]
    public async Task Atelier0741_FiresOnRawScopeLiteral()
    {
        const string source = """
            using Atelier.Framework.Attributes;

            namespace Atelier.Framework.Identity.Authorization
            {
                public static class Scopes
                {
                    public static class Boutique
                    {
                        public const string WRITE = "atelier.boutique.write";
                    }
                }
            }

            namespace Sample
            {
                [RequiresScope("atelier.boutique.write")]
                public sealed class BoutiqueService
                {
                }
            }
            """;

        var expected = Scope0741(16, 20, 16, 44, "\"atelier.boutique.write\"");
        await AnalyzerVerify.FiresAsync<ClosedSetAuthorizationLiteralAnalyzer>(source, expected);
    }

    [Fact]
    public async Task Atelier0741_SilentOnNestedCatalogScopeConstant()
    {
        const string source = """
            using Atelier.Framework.Attributes;
            using Atelier.Framework.Identity.Authorization;

            namespace Atelier.Framework.Identity.Authorization
            {
                public static class Scopes
                {
                    public static class Boutique
                    {
                        public const string READ = "atelier.boutique.read";
                        public const string WRITE = "atelier.boutique.write";
                    }
                }
            }

            namespace Sample
            {
                [RequiresScope(Scopes.Boutique.WRITE)]
                public sealed class BoutiqueService
                {
                }
            }
            """;

        await AnalyzerVerify.SilentAsync<ClosedSetAuthorizationLiteralAnalyzer>(source);
    }

    [Fact]
    public async Task Atelier0741_SilentOnNestedCatalogScopeConstantInRequiresScopeContract()
    {
        const string source = """
            using Atelier.Framework.Attributes;
            using Atelier.Framework.Identity.Authorization;

            namespace Atelier.Framework.Identity.Authorization
            {
                public static class Scopes
                {
                    public static class Boutique
                    {
                        public const string WRITE = "atelier.boutique.write";
                    }
                }
            }

            namespace Sample
            {
                [RequiresScopeContract(Scopes.Boutique.WRITE)]
                public sealed class BoutiqueService
                {
                }
            }
            """;

        await AnalyzerVerify.SilentAsync<ClosedSetAuthorizationLiteralAnalyzer>(source);
    }

    [Fact]
    public async Task Atelier0741_FiresOnTypoedNearMissScopeLiteral()
    {
        const string source = """
            using Atelier.Framework.Attributes;

            namespace Atelier.Framework.Identity.Authorization
            {
                public static class Scopes
                {
                    public static class Boutique
                    {
                        public const string WRITE = "atelier.boutique.write";
                    }
                }
            }

            namespace Sample
            {
                [RequiresScope("atelier.boutique.writ")]
                public sealed class BoutiqueService
                {
                }
            }
            """;

        var expected = Scope0741(16, 20, 16, 43, "\"atelier.boutique.writ\"");
        await AnalyzerVerify.FiresAsync<ClosedSetAuthorizationLiteralAnalyzer>(source, expected);
    }

    [Fact]
    public async Task Atelier0741_FiresOnRawApiClaimLiteral()
    {
        const string source = """
            using Atelier.Framework.Attributes;

            namespace Atelier.Framework.Identity.Authorization
            {
                public static class Claims
                {
                    public const string BOUTIQUE_READ = "atelier.boutique.read";
                }
            }

            namespace Sample
            {
                [Api(new[] { "atelier.boutique.read" })]
                public sealed class BoutiqueService
                {
                }
            }
            """;

        var expected = Claim0741(13, 18, 13, 41, "\"atelier.boutique.read\"");
        await AnalyzerVerify.FiresAsync<ClosedSetAuthorizationLiteralAnalyzer>(source, expected);
    }

    [Fact]
    public async Task Atelier0741_SilentOnApiClaimArrayOfCatalogConstants()
    {
        const string source = """
            using Atelier.Framework.Attributes;
            using Atelier.Framework.Identity.Authorization;

            namespace Atelier.Framework.Identity.Authorization
            {
                public static class Claims
                {
                    public const string BOUTIQUE_READ = "atelier.boutique.read";
                }
            }

            namespace Sample
            {
                [Api(new[] { Claims.BOUTIQUE_READ })]
                public sealed class BoutiqueService
                {
                }
            }
            """;

        await AnalyzerVerify.SilentAsync<ClosedSetAuthorizationLiteralAnalyzer>(source);
    }

    [Fact]
    public async Task Atelier0741_FiresOnMixedApiClaimArrayWithOneRawLiteral()
    {
        const string source = """
            using Atelier.Framework.Attributes;
            using Atelier.Framework.Identity.Authorization;

            namespace Atelier.Framework.Identity.Authorization
            {
                public static class Claims
                {
                    public const string BOUTIQUE_READ = "atelier.boutique.read";
                }
            }

            namespace Sample
            {
                [Api(new[] { Claims.BOUTIQUE_READ, "atelier.boutique.admin" })]
                public sealed class BoutiqueService
                {
                }
            }
            """;

        var expected = Claim0741(14, 40, 14, 64, "\"atelier.boutique.admin\"");
        await AnalyzerVerify.FiresAsync<ClosedSetAuthorizationLiteralAnalyzer>(source, expected);
    }

    [Fact]
    public async Task Atelier0741_FiresWhenScopeAssembledByConcatenationOfCatalogConstants()
    {
        const string source = """
            using Atelier.Framework.Attributes;
            using Atelier.Framework.Identity.Authorization;

            namespace Atelier.Framework.Identity.Authorization
            {
                public static class Scopes
                {
                    public static class Boutique
                    {
                        public const string READ = "atelier.boutique.read";
                    }
                }
            }

            namespace Sample
            {
                [RequiresScope(Scopes.Boutique.READ + ".elevated")]
                public sealed class BoutiqueService
                {
                }
            }
            """;

        var expected = Scope0741(17, 20, 17, 54, "Scopes.Boutique.READ + \".elevated\"");
        await AnalyzerVerify.FiresAsync<ClosedSetAuthorizationLiteralAnalyzer>(source, expected);
    }

    [Fact]
    public async Task Atelier0741_FiresWhenScopeIsConstFromNonCatalogClassSharingSimpleName()
    {
        const string source = """
            using Atelier.Framework.Attributes;

            namespace Sample.Fake
            {
                public static class Scopes
                {
                    public const string WRITE = "atelier.boutique.write";
                }
            }

            namespace Sample
            {
                [RequiresScope(Sample.Fake.Scopes.WRITE)]
                public sealed class BoutiqueService
                {
                }
            }
            """;

        var expected = Scope0741(13, 20, 13, 44, "Sample.Fake.Scopes.WRITE");
        await AnalyzerVerify.FiresAsync<ClosedSetAuthorizationLiteralAnalyzer>(source, expected);
    }

    [Fact]
    public async Task Atelier0741_FiresWhenClaimIsConstFromUnrelatedClassWithCatalogValue()
    {
        const string source = """
            using Atelier.Framework.Attributes;

            namespace Sample
            {
                public static class Permissions
                {
                    public const string BoutiqueRead = "atelier.boutique.read";
                }

                public sealed class Reader
                {
                    [RequiresClaim(Permissions.BoutiqueRead)]
                    public string Read()
                    {
                        return "x";
                    }
                }
            }
            """;

        var expected = Claim0741(12, 24, 12, 48, "Permissions.BoutiqueRead");
        await AnalyzerVerify.FiresAsync<ClosedSetAuthorizationLiteralAnalyzer>(source, expected);
    }

    [Fact]
    public async Task Atelier0741_SilentWhenRequiresScopeUsesScopeCatalogButClaimCatalogIsTheWrongFamily()
    {
        const string source = """
            using Atelier.Framework.Attributes;
            using Atelier.Framework.Identity.Authorization;

            namespace Atelier.Framework.Identity.Authorization
            {
                public static class Scopes
                {
                    public static class Boutique
                    {
                        public const string READ = "atelier.boutique.read";
                    }
                }
            }

            namespace Sample
            {
                [RequiresScope(Scopes.Boutique.READ)]
                public sealed class BoutiqueService
                {
                }
            }
            """;

        await AnalyzerVerify.SilentAsync<ClosedSetAuthorizationLiteralAnalyzer>(source);
    }

    [Fact]
    public async Task Atelier0741_FiresWhenScopeLiteralPassedToClaimFamilyAttribute()
    {
        const string source = """
            using Atelier.Framework.Attributes;
            using Atelier.Framework.Identity.Authorization;

            namespace Atelier.Framework.Identity.Authorization
            {
                public static class Scopes
                {
                    public static class Boutique
                    {
                        public const string WRITE = "atelier.boutique.write";
                    }
                }
            }

            namespace Sample
            {
                public sealed class Reader
                {
                    [RequiresClaim(Scopes.Boutique.WRITE)]
                    public string Read()
                    {
                        return "x";
                    }
                }
            }
            """;

        var expected = Claim0741(19, 24, 19, 45, "Scopes.Boutique.WRITE");
        await AnalyzerVerify.FiresAsync<ClosedSetAuthorizationLiteralAnalyzer>(source, expected);
    }

    [Fact]
    public async Task Atelier0741_SilentWhenDescriptionNamedArgumentIsRawString()
    {
        const string source = """
            using Atelier.Framework.Attributes;
            using Atelier.Framework.Identity.Authorization;

            namespace Atelier.Framework.Identity.Authorization
            {
                public static class Scopes
                {
                    public static class Boutique
                    {
                        public const string WRITE = "atelier.boutique.write";
                    }
                }
            }

            namespace Sample
            {
                [RequiresScope(Scopes.Boutique.WRITE, Description = "free-form prose")]
                public sealed class BoutiqueService
                {
                }
            }
            """;

        await AnalyzerVerify.SilentAsync<ClosedSetAuthorizationLiteralAnalyzer>(source);
    }
}
