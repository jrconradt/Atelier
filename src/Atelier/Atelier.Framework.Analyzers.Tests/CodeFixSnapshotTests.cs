using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Atelier.Framework.Analyzers.Tests;

public sealed class CodeFixSnapshotTests
{
    [Fact]
    public async Task Atelier1600_RemovesRedundantConstructor()
    {
        const string before = """
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

        const string after = """
            using Atelier.Framework.Requisitions;

            namespace Sample;

            public interface IClock
            {
            }

            public partial class TimedService
            {
                [Requisite] private readonly IClock _clock = null!;
            }
            """;

        var expected = new DiagnosticResult("ATELIER1600", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithSpan(12, 5, 14, 6)
            .WithArguments("TimedService");

        await AnalyzerVerify.CodeFixAsync<RedundantConstructorAnalyzer, RedundantConstructorCodeFixProvider>(
            before,
            after,
            expected);
    }

    [Fact]
    public async Task Atelier1600_RemovesRedundantConstructorWithExplicitBaseType()
    {
        const string before = """
            using Atelier.Framework.Requisitions;

            namespace Sample;

            public interface IClock
            {
            }

            public abstract class ServiceBase
            {
                protected ServiceBase()
                {
                }
            }

            public partial class TimedService : ServiceBase
            {
                [Requisite] private readonly IClock _clock = null!;
                public TimedService(IClock clock) : base()
                {
                }
            }
            """;

        const string after = """
            using Atelier.Framework.Requisitions;

            namespace Sample;

            public interface IClock
            {
            }

            public abstract class ServiceBase
            {
                protected ServiceBase()
                {
                }
            }

            public partial class TimedService : ServiceBase
            {
                [Requisite] private readonly IClock _clock = null!;
            }
            """;

        var expected = new DiagnosticResult("ATELIER1600", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithSpan(19, 5, 21, 6)
            .WithArguments("TimedService");

        await AnalyzerVerify.CodeFixAsync<RedundantConstructorAnalyzer, RedundantConstructorCodeFixProvider>(
            before,
            after,
            expected);
    }
}
