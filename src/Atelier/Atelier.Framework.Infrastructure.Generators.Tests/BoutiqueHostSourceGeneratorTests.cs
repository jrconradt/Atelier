using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Atelier.Framework.Infrastructure.Generators.Tests;

public sealed class BoutiqueHostSourceGeneratorTests
{
    private const string OfferingSource = """
        using Atelier.Framework.Attributes;
        using Atelier.Framework.Primitives;

        namespace Microsoft.Extensions.Hosting
        {
            public interface IHostedService { }
        }

        namespace Atelier.Framework.Offering.Product
        {
            public abstract class ProductBase { }
        }

        namespace Atelier.Framework.Offering
        {
            public interface IWidget { }

            [Infrastructure]
            public class Widget : IWidget { }

            [Infrastructure(InfrastructureLifetime.Scoped)]
            public class Gadget { }

            [Infrastructure]
            public class Pump : Microsoft.Extensions.Hosting.IHostedService { }

            [Infrastructure]
            internal class Hidden { }

            [Api(null)]
            public class CatalogService { }

            public static class CatalogServiceApiEndpoints
            {
                public static void MapCatalogServiceEndpoints(object endpoints) { }
            }

            [Api(null)]
            public class UnmappedService { }
        }
        """;

    private const string ProductSource = """
        using Atelier.Framework.Attributes;

        namespace Atelier.Sample.Products;

        public class OrdersProduct : Atelier.Framework.Offering.Product.ProductBase { }

        public class NotAProduct { }

        [Infrastructure]
        public class OrderLedger { }

        [Infrastructure]
        public class Safe { }
        """;

    private const string UnrelatedSource = """
        using Atelier.Framework.Attributes;

        namespace Atelier.Sample.Unrelated;

        [Infrastructure]
        public class Stray { }
        """;

    private static readonly MetadataReference PrimitivesReference =
        MetadataReference.CreateFromFile(typeof(Atelier.Framework.Primitives.InfrastructureLifetime).Assembly.Location);

    [Fact]
    public void WithoutBoutiqueAttribute_EmitsNothing()
    {
        var (generated, diagnostics) = Run("public class Plain { }");

        Assert.Null(generated);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EmitsEntryPoint_WithBoutiqueIdentity()
    {
        var (generated, _) = Run("""
            [assembly: Atelier.Framework.Attributes.Boutique("orders", Name = "Orders", Version = "2.1.0")]
            """);

        Assert.NotNull(generated);
        Assert.Contains("namespace Atelier.Sample.Host;", generated);
        Assert.Contains("public static global::System.Threading.Tasks.Task Main(string[] args)", generated);
        Assert.Contains("id: \"orders\",", generated);
        Assert.Contains("name: \"Orders\",", generated);
        Assert.Contains("description: null,", generated);
        Assert.Contains("version: \"2.1.0\",", generated);
    }

    [Fact]
    public void RegistersCoreAssemblyInfrastructure_ByInterfaceConventionAndLifetime()
    {
        var (generated, _) = Run("""[assembly: Atelier.Framework.Attributes.Boutique("orders")]""");

        Assert.Contains("services.AddSingleton<global::Atelier.Framework.Offering.IWidget, global::Atelier.Framework.Offering.Widget>();", generated);
        Assert.Contains("services.AddScoped<global::Atelier.Framework.Offering.Gadget>();", generated);
        Assert.DoesNotContain("Hidden", generated);
    }

    [Fact]
    public void RegistersSingletonHostedServices_AsHostedServices()
    {
        var (generated, _) = Run("""[assembly: Atelier.Framework.Attributes.Boutique("orders")]""");

        Assert.Contains("services.AddSingleton<global::Atelier.Framework.Offering.Pump>();", generated);
        Assert.Contains("services.AddHostedService(sp => (global::Atelier.Framework.Offering.Pump)sp.GetRequiredService<global::Atelier.Framework.Offering.Pump>());", generated);
    }

    [Fact]
    public void MapsApiEndpoints_OnlyWhereTheEndpointsClassExists()
    {
        var (generated, _) = Run("""[assembly: Atelier.Framework.Attributes.Boutique("orders")]""");

        Assert.Contains("global::Atelier.Framework.Offering.CatalogServiceApiEndpoints.MapCatalogServiceEndpoints(endpoints);", generated);
        Assert.DoesNotContain("UnmappedService", generated);
    }

    [Fact]
    public void IgnoresAtelierAssemblies_OutsideTheCoreSetAndProductClosure()
    {
        var (generated, _) = Run("""[assembly: Atelier.Framework.Attributes.Boutique("orders")]""");

        Assert.DoesNotContain("Stray", generated);
        Assert.DoesNotContain("OrderLedger", generated);
    }

    [Fact]
    public void DeclaredProduct_EntersTheManifest_AndBringsItsAssemblyIntoRegistration()
    {
        var (generated, diagnostics) = Run("""
            [assembly: Atelier.Framework.Attributes.Boutique("orders")]
            [assembly: Atelier.Framework.Attributes.BoutiqueProduct(typeof(Atelier.Sample.Products.OrdersProduct), AutoStart = false)]
            """);

        Assert.Empty(diagnostics);
        Assert.Contains("ProductTypeName = \"Atelier.Sample.Products.OrdersProduct\", ProductType = typeof(global::Atelier.Sample.Products.OrdersProduct), AutoStart = false", generated);
        Assert.Contains("services.AddSingleton<global::Atelier.Sample.Products.OrderLedger>();", generated);
        Assert.DoesNotContain("Stray", generated);
    }

    [Fact]
    public void ProductNotDerivedFromProductBase_ReportsAtelier0900()
    {
        var (generated, diagnostics) = Run("""
            [assembly: Atelier.Framework.Attributes.Boutique("orders")]
            [assembly: Atelier.Framework.Attributes.BoutiqueProduct(typeof(Atelier.Sample.Products.NotAProduct))]
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("ATELIER0900", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(1, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
        Assert.DoesNotContain("NotAProduct", generated);
    }

    [Fact]
    public void RerunOnUnchangedInput_ReusesTheCachedOutput()
    {
        var compilation = CreateHostCompilation("""[assembly: Atelier.Framework.Attributes.Boutique("orders")]""");
        var generator = new BoutiqueHostSourceGenerator().AsSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { generator },
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation);
        driver = driver.RunGenerators(compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("public class Unrelated { }")));

        var outputs = driver.GetRunResult().Results.Single().TrackedOutputSteps
            .SelectMany(step => step.Value)
            .SelectMany(run => run.Outputs)
            .ToList();
        Assert.NotEmpty(outputs);
        Assert.All(outputs, output => Assert.Equal(IncrementalStepRunReason.Cached, output.Reason));
    }

    [Fact]
    public void EmbedsRequisiteManifest_WithTheAtelierAssemblyClosure()
    {
        var (generated, _) = Run("""[assembly: Atelier.Framework.Attributes.Boutique("orders")]""");

        Assert.Contains("new global::Atelier.Framework.Attache.Hosting.BoutiqueArtifact(\"requisite-manifest.json\"", generated);
        Assert.Contains(@"\""boutique\"": \""orders\""", generated);
        var requisites = System.Text.RegularExpressions.Regex.Match(generated!, @"\\""requisiteAssemblies\\"": \[([^\]]*)\]").Groups[1].Value;
        Assert.Equal(
            @"\""Atelier.Framework.Attributes\"", \""Atelier.Framework.Offering\"", \""Atelier.Framework.Outcomes\"", \""Atelier.Framework.Primitives\"", \""Atelier.Sample.Host\"", \""Atelier.Sample.Products\"", \""Atelier.Sample.Unrelated\""",
            requisites);
    }

    [Fact]
    public void EmitsOnlyTheRequisiteManifest_AsHostArtifacts()
    {
        var (generated, _) = Run("""
            [assembly: Atelier.Framework.Attributes.Boutique("orders")]
            [assembly: Atelier.Framework.Attributes.BoutiqueProduct(typeof(Atelier.Sample.Products.OrdersProduct))]
            """);

        Assert.Contains("\"requisite-manifest.json\"", generated);
        Assert.DoesNotContain("network-zones.json", generated);
        Assert.DoesNotContain("network-policies.yaml", generated);
    }

    private static (string? Generated, IReadOnlyList<Diagnostic> Diagnostics) Run(string hostSource)
    {
        var compilation = CreateHostCompilation(hostSource);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new BoutiqueHostSourceGenerator());
        driver = driver.RunGenerators(compilation);
        var result = driver.GetRunResult();
        var generated = result.GeneratedTrees.SingleOrDefault()?.GetText().ToString();
        return (generated, result.Diagnostics);
    }

    private static CSharpCompilation CreateHostCompilation(string hostSource)
    {
        var offering = EmitLibrary("Atelier.Framework.Offering", OfferingSource);
        var products = EmitLibrary("Atelier.Sample.Products", ProductSource, offering);
        var unrelated = EmitLibrary("Atelier.Sample.Unrelated", UnrelatedSource);

        return CompilationFactory.Create(hostSource, "Atelier.Sample.Host")
            .AddReferences(PrimitivesReference, offering, products, unrelated);
    }

    private static MetadataReference EmitLibrary(string assemblyName, string source, params MetadataReference[] references)
    {
        var compilation = CompilationFactory.Create(source, assemblyName)
            .AddReferences(PrimitivesReference)
            .AddReferences(references);
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }
}
