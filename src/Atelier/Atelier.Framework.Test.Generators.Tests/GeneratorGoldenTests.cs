using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Atelier.Framework.Test.Generators.Tests;

public sealed class GeneratorGoldenTests
{
    private const string FixtureSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using Atelier.Framework.Attributes;
        using Atelier.Framework.Observability;
        using Atelier.Framework.Outcomes;
        using Atelier.Framework.Requisitions;

        namespace Sample;

        public interface IClock
        {
        }

        public partial class GreeterService : IAtelier
        {
            [Requisite] private readonly IClock _clock = null!;

            [Operation("greet")]
            public Task<Outcome<string>> GreetAsync(string name, CancellationToken cancellationToken)
                => Task.FromResult(Outcome<string>.Success(name));

            public void Observe(
                LogLevel level = LogLevel.Information,
                System.Exception? exception = null,
                string? message = null,
                params System.ReadOnlySpan<(string Key, object Value)> values)
            {
            }
        }
        """;

    private const string ProductFixtureSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using Atelier.Framework.Observability;
        using Atelier.Framework.Offering.Product;
        using Atelier.Framework.Offering.Product.Configuration;
        using Atelier.Framework.Outcomes;

        namespace Sample;

        public partial class GreeterProduct : ProductBase
        {
            protected override void ConfigureOfferings(IOfferingConfiguration offerings)
            {
                offerings.AddOffering<GreeterOffering>();
            }

            protected override Task<Outcome> OnStartAsync(CancellationToken cancellationToken)
                => Task.FromResult(Outcome.Success());

            protected override Task<Outcome> OnStopAsync(CancellationToken cancellationToken)
                => Task.FromResult(Outcome.Success());
        }

        public sealed class GreeterOffering : global::Atelier.Framework.Offering.IOffering
        {
            public void Start()
            {
            }

            public void Stop()
            {
            }

            public bool IsRunning => false;
        }
        """;

    private static readonly string[] ExpectedInvariants =
    {
        "DI-Wiring/Ctor-Exists",
        "DI-Wiring/All-Fields-Wired",
        "DI-Wiring/Logger-Wired",
        "IAtelier/Observe-Surface-Present",
        "IAtelier/Logger-Surface-Present",
        "Operation/No-Throw-On-Default-Input",
        "Operation/Happy-Path-Success",
        "Operation/Cancellation-Honored",
        "Operation/Null-Param-Honored",
        "Operation/Empty-String-Tolerated",
        "Operation/Concurrent-Invocation-Safe",
    };

    private static readonly string[] ExpectedProductInvariants =
    {
        "DI-Wiring/Ctor-Exists",
        "DI-Wiring/All-Fields-Wired",
        "DI-Wiring/Logger-Wired",
        "IAtelier/Observe-Surface-Present",
        "IAtelier/Logger-Surface-Present",
        "Lifecycle/Product-Configure-Start-Stop-Succeeds",
    };

    private static GeneratorDriverRunResult Run()
        => RunOver(FixtureSource);

    private static GeneratorDriverRunResult RunProduct()
        => RunOver(ProductFixtureSource);

    private static GeneratorDriverRunResult RunOver(string source)
    {
        var compilation = CompilationFactory.Create(source);
        var generator = new TestSourceGenerator().AsSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGenerators(compilation);
        return driver.GetRunResult();
    }

    [Fact]
    public void EmitsSingleSidecar_WithStableHintName_AndNoDiagnostics()
    {
        var result = Run();

        Assert.Empty(result.Diagnostics);
        var generated = Assert.Single(result.GeneratedTrees);
        Assert.EndsWith("GreeterService_GeneratedTests.g.cs", generated.FilePath);
    }

    [Fact]
    public void Sidecar_CarriesTracedResolution_ForOperationTests()
    {
        var actual = Assert.Single(Run().GeneratedTrees).GetText().ToString();

        Assert.Contains("\"GreetAsync_Traced\"", actual);
        Assert.Contains("\"GreetAsync_Validated\"", actual);
    }

    [Fact]
    public void Sidecar_EmitsEveryExpectedInvariant()
    {
        var actual = Assert.Single(Run().GeneratedTrees).GetText().ToString();

        Assert.Contains("internal static class GreeterService_GeneratedTests", actual);
        Assert.Contains("private static bool IsAtelierOutcome(", actual);

        foreach (var invariant in ExpectedInvariants)
        {
            Assert.Contains($"[GeneratedTest(\"{invariant}\"", actual);
        }
    }

    [Fact]
    public void Sidecar_PinnedToGolden()
    {
        var actual = Normalize(Assert.Single(Run().GeneratedTrees).GetText().ToString());
        var goldenPath = Path.Combine(GoldenDirectory(), "GreeterService_GeneratedTests.g.verified.txt");

        if (!File.Exists(goldenPath))
        {
            Assert.Fail($"Golden file is missing at '{goldenPath}'. Update it through a reviewed change to the tracked baseline.");
        }

        var expected = Normalize(File.ReadAllText(goldenPath));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ProductSidecar_EmitsLifecycleInvariant()
    {
        var actual = Assert.Single(RunProduct().GeneratedTrees).GetText().ToString();

        Assert.Contains("internal static class GreeterProduct_GeneratedTests", actual);

        foreach (var invariant in ExpectedProductInvariants)
        {
            Assert.Contains($"[GeneratedTest(\"{invariant}\"", actual);
        }
    }

    [Fact]
    public void ProductSidecar_PinnedToGolden()
    {
        var actual = Normalize(Assert.Single(RunProduct().GeneratedTrees).GetText().ToString());
        var goldenPath = Path.Combine(GoldenDirectory(), "GreeterProduct_GeneratedTests.g.verified.txt");

        if (!File.Exists(goldenPath))
        {
            Assert.Fail($"Golden file is missing at '{goldenPath}'. Update it through a reviewed change to the tracked baseline.");
        }

        var expected = Normalize(File.ReadAllText(goldenPath));
        Assert.Equal(expected, actual);
    }

    private static string Normalize(string text)
        => text.Replace("\r\n", "\n").TrimEnd('\n');

    private static string GoldenDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Atelier.Framework.Test.Generators.Tests.csproj")))
            {
                return Path.Combine(current.FullName, "Golden");
            }
            current = current.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "Golden");
    }
}
