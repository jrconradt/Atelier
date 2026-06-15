using Atelier.Framework.Primitives;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Atelier.Framework.Analyzers.Tests;

public sealed class AnalyzerDiagnosticTests
{
    [Fact]
    public async Task Atelier0010_FiresWhenOperationDereferencesUnguardedParameter()
    {
        const string source = """
            using Atelier.Framework.Attributes;

            namespace Sample;

            public sealed class Greeter
            {
                [Operation("greet")]
                public string Greet(string name)
                {
                    return name.ToUpperInvariant();
                }
            }
            """;

        var expected = new DiagnosticResult("ATELIER0010", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithSpan(8, 19, 8, 24)
            .WithArguments("Greet", "name");

        await AnalyzerVerify.FiresAsync<OperationParameterGuardAnalyzer>(source, expected);
    }

    [Fact]
    public async Task Atelier0010_SilentWhenParameterIsGuarded()
    {
        const string source = """
            using System;
            using Atelier.Framework.Attributes;

            namespace Sample;

            public sealed class Greeter
            {
                [Operation("greet")]
                public string Greet(string name)
                {
                    ArgumentNullException.ThrowIfNull(name);
                    return name.ToUpperInvariant();
                }
            }
            """;

        await AnalyzerVerify.SilentAsync<OperationParameterGuardAnalyzer>(source);
    }

    [Fact]
    public async Task Atelier0400_FiresWhenSingletonHasMutableState()
    {
        const string source = """
            using Atelier.Framework.Attributes;

            namespace Sample;

            [Infrastructure(Atelier.Framework.Primitives.InfrastructureLifetime.Singleton)]
            public sealed class CounterService
            {
                private int _count;
            }
            """;

        var expected = new DiagnosticResult("ATELIER0400", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithSpan(6, 21, 6, 35)
            .WithArguments("CounterService");

        await AnalyzerVerify.FiresAsync<InfrastructureLifetimeAnalyzer>(source, expected);
    }

    [Fact]
    public async Task Atelier0400_SilentWhenSingletonStateIsReadonly()
    {
        const string source = """
            using Atelier.Framework.Attributes;

            namespace Sample;

            [Infrastructure(Atelier.Framework.Primitives.InfrastructureLifetime.Singleton)]
            public sealed class ClockService
            {
                private readonly string _name = "clock";
            }
            """;

        await AnalyzerVerify.SilentAsync<InfrastructureLifetimeAnalyzer>(source);
    }

    [Fact]
    public async Task Atelier1600_FiresOnRedundantConstructorInRequisitePartial()
    {
        const string source = """
            using Atelier.Framework.Requisitions;

            namespace Sample;

            public interface IClock
            {
            }

            public partial class TimedService
            {
                [Requisite] private readonly IClock _clock = null!;

                public TimedService(IClock clock) : base()
                {
                }
            }
            """;

        var expected = new DiagnosticResult("ATELIER1600", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithSpan(13, 5, 15, 6)
            .WithArguments("TimedService");

        await AnalyzerVerify.FiresAsync<RedundantConstructorAnalyzer>(source, expected);
    }

    [Fact]
    public async Task Atelier1600_SilentWhenConstructorHasBody()
    {
        const string source = """
            using Atelier.Framework.Requisitions;

            namespace Sample;

            public interface IClock
            {
            }

            public partial class TimedService
            {
                [Requisite] private readonly IClock _clock = null!;

                public TimedService(IClock clock)
                {
                    _clock = clock;
                }
            }
            """;

        await AnalyzerVerify.SilentAsync<RedundantConstructorAnalyzer>(source);
    }

    [Fact]
    public async Task Atelier1402_FiresWhenRequisiteTargetLacksInfrastructure()
    {
        const string source = """
            using Atelier.Framework.Requisitions;

            namespace Sample;

            public sealed class PaymentGateway
            {
            }

            public sealed class CheckoutService
            {
                [Requisite] private readonly PaymentGateway _gateway = null!;
            }
            """;

        var expected = new DiagnosticResult("ATELIER1402", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithSpan(5, 21, 5, 35)
            .WithArguments("PaymentGateway");

        await AnalyzerVerify.FiresAsync<InfrastructureAttributeRequiredAnalyzer>(source, expected);
    }

    [Fact]
    public async Task Atelier1402_SilentWhenRequisiteTargetHasInfrastructure()
    {
        const string source = """
            using Atelier.Framework.Attributes;
            using Atelier.Framework.Requisitions;

            namespace Sample;

            [Infrastructure(Atelier.Framework.Primitives.InfrastructureLifetime.Scoped)]
            public sealed class PaymentGateway
            {
            }

            public sealed class CheckoutService
            {
                [Requisite] private readonly PaymentGateway _gateway = null!;
            }
            """;

        await AnalyzerVerify.SilentAsync<InfrastructureAttributeRequiredAnalyzer>(source);
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

        var expected = new DiagnosticResult("ATELIER0741", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithSpan(15, 24, 15, 47)
            .WithArguments("\"atelier.boutique.read\"", "Atelier.Framework.Identity.Authorization.Claims");

        await AnalyzerVerify.FiresAsync<ClosedSetAuthorizationLiteralAnalyzer>(source, expected);
    }

    [Fact]
    public async Task Atelier0741_SilentOnCatalogClaimConstant()
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
                    public const string REPORTS_READ = "reports.read";
                }
            }

            namespace Sample
            {
                [RequiresScope("reports.read")]
                public sealed class ReportService
                {
                }
            }
            """;

        var expected = new DiagnosticResult("ATELIER0741", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithSpan(13, 20, 13, 34)
            .WithArguments("\"reports.read\"", "Atelier.Framework.Identity.Authorization.Scopes");

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
    public async Task Atelier0750_FiresOnWriteEffectApiMethodWithoutScope()
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
            }
            """;

        var expected = new DiagnosticResult("ATELIER0750", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithSpan(10, 17, 10, 36)
            .WithArguments("CreateBoutiqueAsync", "BoutiqueService");

        await AnalyzerVerify.FiresAsync<MutatingApiScopeAnalyzer>(source, expected);
    }

    [Fact]
    public async Task Atelier0750_SilentOnReadEffectApiMethod()
    {
        const string source = """
            using System.Threading.Tasks;
            using Atelier.Framework.Attributes;

            namespace Sample;

            [Api(null)]
            public sealed class BoutiqueService
            {
                [OperationEffect(EffectKind.Read)]
                public Task DiscoverBoutiquesAsync()
                {
                    return Task.CompletedTask;
                }
            }
            """;

        await AnalyzerVerify.SilentAsync<MutatingApiScopeAnalyzer>(source);
    }

    [Fact]
    public async Task Atelier0750_SilentWhenTypeHasScopeResource()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Atelier.Framework.Attributes;

            namespace Sample;

            [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, Inherited = true)]
            public sealed class ScopeResourceAttribute : Attribute
            {
                public ScopeResourceAttribute(Type scopePairType)
                {
                }
            }

            public static class BoutiqueScopes
            {
                public const string READ = "atelier.boutique.read";
                public const string WRITE = "atelier.boutique.write";
            }

            [Api(null)]
            [ScopeResource(typeof(BoutiqueScopes))]
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
    public async Task Atelier0750_SilentWhenMethodHasExplicitScope()
    {
        const string source = """
            using System.Threading.Tasks;
            using Atelier.Framework.Attributes;

            namespace Sample;

            [Api(null)]
            public sealed class BoutiqueService
            {
                [OperationEffect(EffectKind.Write)]
                [RequiresScope("atelier.boutique.write")]
                public Task CreateBoutiqueAsync()
                {
                    return Task.CompletedTask;
                }
            }
            """;

        await AnalyzerVerify.SilentAsync<MutatingApiScopeAnalyzer>(source);
    }

    [Fact]
    public async Task Atelier0750_SilentWhenMethodIsAnonymous()
    {
        const string source = """
            using System.Threading.Tasks;
            using Atelier.Framework.Attributes;

            namespace Sample;

            [Api(null)]
            public sealed class BoutiqueService
            {
                [OperationEffect(EffectKind.Write)]
                [AllowAnonymous]
                public Task CreateBoutiqueAsync()
                {
                    return Task.CompletedTask;
                }
            }
            """;

        await AnalyzerVerify.SilentAsync<MutatingApiScopeAnalyzer>(source);
    }

    [Fact]
    public async Task Atelier0750_FiresOnWriteEffectApiMethodWithNonTaskReturn()
    {
        const string source = """
            using Atelier.Framework.Attributes;

            namespace Sample;

            [Api(null)]
            public sealed class BoutiqueService
            {
                [OperationEffect(EffectKind.Write)]
                public string CreateBoutique()
                {
                    return "x";
                }
            }
            """;

        var expected = new DiagnosticResult("ATELIER0750", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithSpan(9, 19, 9, 33)
            .WithArguments("CreateBoutique", "BoutiqueService");

        await AnalyzerVerify.FiresAsync<MutatingApiScopeAnalyzer>(source, expected);
    }

    [Fact]
    public async Task Atelier0751_FiresOnScopeResourceOperationWithoutEffect()
    {
        const string source = """
            using Atelier.Framework.Attributes;

            namespace Sample;

            public static class BoutiqueScopes
            {
                public const string READ = "atelier.boutique.read";
                public const string WRITE = "atelier.boutique.write";
            }

            [ScopeResource(typeof(BoutiqueScopes))]
            public sealed class BoutiqueService
            {
                public string GetOrCreate()
                {
                    return "x";
                }
            }
            """;

        var expected = new DiagnosticResult("ATELIER0751", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithSpan(14, 19, 14, 30)
            .WithArguments("GetOrCreate", "BoutiqueService");

        await AnalyzerVerify.FiresAsync<OperationEffectRequiredAnalyzer>(source, expected);
    }

    [Fact]
    public async Task Atelier0751_FiresOnInterfaceCarriedScopeResourceOperationWithoutEffect()
    {
        const string source = """
            using Atelier.Framework.Attributes;

            namespace Sample;

            public static class BoutiqueScopes
            {
                public const string READ = "atelier.boutique.read";
                public const string WRITE = "atelier.boutique.write";
            }

            [ScopeResource(typeof(BoutiqueScopes))]
            public interface IBoutiqueContract
            {
                void ListAndArchive();
            }
            """;

        var expected = new DiagnosticResult("ATELIER0751", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithSpan(14, 10, 14, 24)
            .WithArguments("ListAndArchive", "IBoutiqueContract");

        await AnalyzerVerify.FiresAsync<OperationEffectRequiredAnalyzer>(source, expected);
    }

    [Fact]
    public async Task Atelier0751_SilentWhenOperationDeclaresEffect()
    {
        const string source = """
            using Atelier.Framework.Attributes;

            namespace Sample;

            public static class BoutiqueScopes
            {
                public const string READ = "atelier.boutique.read";
                public const string WRITE = "atelier.boutique.write";
            }

            [ScopeResource(typeof(BoutiqueScopes))]
            public sealed class BoutiqueService
            {
                [OperationEffect(EffectKind.Write)]
                public string GetOrCreate()
                {
                    return "x";
                }
            }
            """;

        await AnalyzerVerify.SilentAsync<OperationEffectRequiredAnalyzer>(source);
    }

    [Fact]
    public async Task Atelier0751_SilentWhenOperationHasExplicitScope()
    {
        const string source = """
            using Atelier.Framework.Attributes;

            namespace Sample;

            public static class BoutiqueScopes
            {
                public const string READ = "atelier.boutique.read";
                public const string WRITE = "atelier.boutique.write";
            }

            [ScopeResource(typeof(BoutiqueScopes))]
            public sealed class BoutiqueService
            {
                [RequiresScope("atelier.boutique.write")]
                public string GetOrCreate()
                {
                    return "x";
                }
            }
            """;

        await AnalyzerVerify.SilentAsync<OperationEffectRequiredAnalyzer>(source);
    }

    [Fact]
    public async Task Atelier0751_SilentWhenTypeIsNotScopeResource()
    {
        const string source = """
            using Atelier.Framework.Attributes;

            namespace Sample;

            public sealed class BoutiqueService
            {
                public string GetOrCreate()
                {
                    return "x";
                }
            }
            """;

        await AnalyzerVerify.SilentAsync<OperationEffectRequiredAnalyzer>(source);
    }

    [Fact]
    public async Task Atelier0752_FiresWhenScopeResourceTargetMissingWriteConst()
    {
        const string source = """
            using Atelier.Framework.Attributes;

            namespace Sample;

            public static class BoutiqueScopes
            {
                public const string READ = "atelier.boutique.read";
            }

            [ScopeResource(typeof(BoutiqueScopes))]
            public sealed class BoutiqueService
            {
            }
            """;

        var expected = new DiagnosticResult("ATELIER0752", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithSpan(10, 23, 10, 37)
            .WithArguments("BoutiqueScopes", "WRITE");

        await AnalyzerVerify.FiresAsync<ScopeResourceCompletenessAnalyzer>(source, expected);
    }

    [Fact]
    public async Task Atelier0752_SilentWhenScopeResourceTargetHasBothConsts()
    {
        const string source = """
            using Atelier.Framework.Attributes;

            namespace Sample;

            public static class BoutiqueScopes
            {
                public const string READ = "atelier.boutique.read";
                public const string WRITE = "atelier.boutique.write";
            }

            [ScopeResource(typeof(BoutiqueScopes))]
            public sealed class BoutiqueService
            {
            }
            """;

        await AnalyzerVerify.SilentAsync<ScopeResourceCompletenessAnalyzer>(source);
    }
}
