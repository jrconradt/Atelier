using System.Collections.Immutable;
using System.Text;
using Templar.Presets;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Atelier.Framework.Offering.Generators;

[Generator]
public sealed class ProductOfferingSourceGenerator : IIncrementalGenerator
{
    private const string OFFERING_ATTRIBUTE_NAME = "OfferingAttribute";
    private const string OFFERING_ATTRIBUTE_FULL_NAME = "Atelier.Framework.Offering.Attributes.OfferingAttribute";
    private const string PRODUCT_ATTRIBUTE_NAME = "ProductAttribute";
    private const string PRODUCT_ATTRIBUTE_FULL_NAME = "Atelier.Framework.Offering.Attributes.ProductAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var productCandidates = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => IsProductCandidate(node),
                static (ctx, _) => (ClassDeclarationSyntax)ctx.Node)
            .Collect();

        var offeringCandidates = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => IsOfferingCandidate(node),
                static (ctx, _) => (ClassDeclarationSyntax)ctx.Node)
            .Collect();

        var combined = productCandidates
            .Combine(offeringCandidates)
            .Combine(context.CompilationProvider);

        context.RegisterSourceOutput(
            combined,
            static (spc, data) =>
                Execute(spc,
                        data.Right,
                        data.Left.Left,
                        data.Left.Right));
    }

    private static bool IsProductCandidate(SyntaxNode node)
    {
        return MatchesAttribute(node,
                                static name =>
                                    name == "Product" ||
                                    name == "ProductAttribute" ||
                                    name.EndsWith(".Product") ||
                                    name.EndsWith(".ProductAttribute"));
    }

    private static bool IsOfferingCandidate(SyntaxNode node)
    {
        return MatchesAttribute(node,
                                static name =>
                                    name == "Offering" ||
                                    name == "OfferingAttribute" ||
                                    name.EndsWith(".Offering") ||
                                    name.EndsWith(".OfferingAttribute"));
    }

    private static bool MatchesAttribute(SyntaxNode node, Func<string, bool> predicate)
    {
        if (node is not ClassDeclarationSyntax classDeclaration)
        {
            return false;
        }

        if (classDeclaration.AttributeLists.Count == 0)
        {
            return false;
        }

        return classDeclaration.AttributeLists
            .SelectMany(al => al.Attributes)
            .Select(a => a.Name.ToString())
            .Any(predicate);
    }

    private static void Execute(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<ClassDeclarationSyntax> productCandidates,
        ImmutableArray<ClassDeclarationSyntax> offeringCandidates)
    {
        var products = new Dictionary<string, INamedTypeSymbol>(StringComparer.OrdinalIgnoreCase);
        foreach (var productClass in productCandidates)
        {
            var semanticModel = compilation.GetSemanticModel(productClass.SyntaxTree);
            var productSymbol = semanticModel.GetDeclaredSymbol(productClass);

            if (productSymbol == null || !InheritsFromProductBase(productSymbol))
            {
                continue;
            }

            var domain = ExtractDomain(productSymbol.ContainingNamespace.ToDisplayString());
            if (domain == null)
            {
                continue;
            }

            if (products.TryGetValue(domain, out var existingProduct)
                && !SymbolEqualityComparer.Default.Equals(existingProduct, productSymbol))
            {
                ReportDiagnostic(
                    context,
                    "PROD006",
                    "Duplicate product domain",
                    $"Products {existingProduct.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} and {productSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} both map to domain '{domain}'. Offering auto-discovery requires a single product per domain.",
                    DiagnosticSeverity.Error,
                    productClass.GetLocation());
                continue;
            }

            products[domain] = productSymbol;
        }

        var offeringsByDomain = new Dictionary<string, List<INamedTypeSymbol>>(StringComparer.OrdinalIgnoreCase);

        foreach (var offeringClass in offeringCandidates)
        {
            var semanticModel = compilation.GetSemanticModel(offeringClass.SyntaxTree);
            var offeringSymbol = semanticModel.GetDeclaredSymbol(offeringClass);

            if (offeringSymbol == null)
            {
                continue;
            }

            var offeringAttr = offeringSymbol
                .GetAttributes()
                .FirstOrDefault(a =>
                    a.AttributeClass?.Name == OFFERING_ATTRIBUTE_NAME ||
                    a.AttributeClass?.ToDisplayString() == OFFERING_ATTRIBUTE_FULL_NAME);

            if (offeringAttr != null)
            {
                var domain = ExtractDomain(offeringSymbol.ContainingNamespace.ToDisplayString());
                if (domain != null)
                {
                    if (!offeringsByDomain.TryGetValue(domain, out var domainOfferings))
                    {
                        domainOfferings = new List<INamedTypeSymbol>();
                        offeringsByDomain[domain] = domainOfferings;
                    }

                    var offeringIdentity = offeringSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var alreadyRegistered = domainOfferings.Any(o =>
                        o.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == offeringIdentity);

                    if (!alreadyRegistered)
                    {
                        domainOfferings.Add(offeringSymbol);
                    }
                }
                continue;
            }

            if (!HasInfrastructureAttribute(offeringSymbol))
            {
                ReportDiagnostic(
                    context,
                    "PROD004",
                    "Offering must have Infrastructure attribute",
                    $"Offering {offeringSymbol.Name} with [Offering] must also have [Infrastructure] attribute",
                    DiagnosticSeverity.Warning,
                    offeringClass.GetLocation());
            }
        }

        foreach (var kvp in products)
        {
            var domain = kvp.Key;
            var productSymbol = kvp.Value;

            if (!offeringsByDomain.TryGetValue(domain, out var offerings) || offerings.Count == 0)
            {
                continue;
            }

            if (HasConfigureOfferingsOverride(productSymbol))
            {
                ReportDiagnostic(
                    context,
                    "PROD005",
                    "Product has manual ConfigureOfferings override",
                    $"Product {productSymbol.Name} has both manual ConfigureOfferings override and [Offering] attributes. Remove manual override to use auto-discovery.",
                    DiagnosticSeverity.Warning,
                    Location.None);
                continue;
            }

            var generatedCode = GenerateProductPartial(productSymbol, offerings, domain);
            var namespacePart = productSymbol.ContainingNamespace.ToDisplayString().Replace(".", "_");
            var fileName = $"{namespacePart}_{productSymbol.Name}.ProductOfferings.g.cs";

            context.AddSource(
                fileName,
                SourceText.From(generatedCode, Encoding.UTF8));
        }
    }

    private static string? ExtractDomain(string namespaceString)
    {
        var parts = namespaceString.Split('.');

        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i] == "Framework" && i < parts.Length - 2)
            {
                var domain = parts[i + 1];
                var suffix = parts[i + 2];

                if (suffix == "Offerings" || suffix == "Products")
                {
                    return domain;
                }
            }
        }

        return null;
    }

    private static bool InheritsFromProductBase(INamedTypeSymbol typeSymbol)
    {
        var current = typeSymbol.BaseType;
        while (current != null)
        {
            if (current.Name == "ProductBase")
            {
                return true;
            }
            current = current.BaseType;
        }
        return false;
    }

    private static bool HasInfrastructureAttribute(INamedTypeSymbol typeSymbol)
    {
        return typeSymbol.GetAttributes().Any(a =>
            a.AttributeClass?.Name == "InfrastructureAttribute");
    }

    private static bool HasConfigureOfferingsOverride(INamedTypeSymbol productType)
    {
        foreach (var syntaxRef in productType.DeclaringSyntaxReferences)
        {
            var syntax = syntaxRef.GetSyntax();
            if (syntax is ClassDeclarationSyntax classSyntax)
            {
                var hasOverride = classSyntax.Members
                    .OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "ConfigureOfferings" &&
                             m.Modifiers.Any(mod => mod.IsKind(SyntaxKind.OverrideKeyword)));

                if (hasOverride)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static string GenerateProductPartial(
        INamedTypeSymbol productType,
        List<INamedTypeSymbol> offerings,
        string domain)
    {
        var addCalls = string.Join("\n",
            offerings
                .OrderBy(o => o.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
                .Select(o => $"        offerings.AddOffering<{o.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}>();"));

        return new ProductOfferingFile
        {
            Namespace = productType.ContainingNamespace.ToDisplayString(),
            Usings = new[] { "Atelier.Framework.Offering.Product.Configuration" },
            Body = $$"""
                public partial class {{productType.Name}}
                {
                    protected override void ConfigureOfferings(IOfferingConfiguration offerings)
                    {
                {{addCalls}}
                    }
                }
                """
        }.Render();
    }

    private sealed class ProductOfferingFile : CSharpFile { }

    private static void ReportDiagnostic(
        SourceProductionContext context,
        string id,
        string title,
        string message,
        DiagnosticSeverity severity,
        Location location)
    {
        var descriptor = new DiagnosticDescriptor(
            id: id,
            title: title,
            messageFormat: message,
            category: "ProductOffering",
            defaultSeverity: severity,
            isEnabledByDefault: true);

        context.ReportDiagnostic(Diagnostic.Create(descriptor, location));
    }
}
