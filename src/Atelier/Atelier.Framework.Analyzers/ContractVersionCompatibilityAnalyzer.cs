using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atelier.Framework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractVersionCompatibilityAnalyzer : DiagnosticAnalyzer
{
    private const string CATEGORY = "Contract";
    private const string DEFAULT_VERSION = "1.0";

    private static readonly DiagnosticDescriptor BackwardCompatibilityNotDeclaredRule = new DiagnosticDescriptor(
        "ATELIER0210",
        "Contract version change must declare backward compatibility",
        "[Contract] '{0}' declares Version '{1}' but does not set IsBackwardCompatible. Set IsBackwardCompatible explicitly so version skew is a deliberate decision, not a silent default.",
        CATEGORY,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "When a [Contract] version moves beyond the initial 1.0, the author must decide whether the new version is backward compatible. The attribute default would otherwise apply silently, which masks breaking schema changes.",
        customTags: new[] { WellKnownDiagnosticTags.Compiler });

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(BackwardCompatibilityNotDeclaredRule);

    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeContractAttribute, SyntaxKind.Attribute);
    }

    private void AnalyzeContractAttribute(SyntaxNodeAnalysisContext context)
    {
        var attribute = (AttributeSyntax)context.Node;

        var attributeSymbol = context.SemanticModel.GetSymbolInfo(attribute).Symbol as IMethodSymbol;
        if (attributeSymbol == null)
        {
            return;
        }

        if (attributeSymbol.ContainingType?.Name != "ContractAttribute")
        {
            return;
        }

        if (attribute.ArgumentList == null)
        {
            return;
        }

        AttributeArgumentSyntax? versionArgument = null;
        var hasBackwardCompatibilityArgument = false;

        foreach (var argument in attribute.ArgumentList.Arguments)
        {
            var name = argument.NameEquals?.Name.Identifier.Text;
            if (name == "Version")
            {
                versionArgument = argument;
            }
            else if (name == "IsBackwardCompatible")
            {
                hasBackwardCompatibilityArgument = true;
            }
        }

        if (versionArgument == null
            || hasBackwardCompatibilityArgument)
        {
            return;
        }

        var constant = context.SemanticModel.GetConstantValue(versionArgument.Expression);
        if (!constant.HasValue
            || constant.Value is not string version)
        {
            return;
        }

        if (string.Equals(version,
                         DEFAULT_VERSION,
                         StringComparison.Ordinal))
        {
            return;
        }

        var typeName = ResolveAttributedTypeName(attribute);

        var diagnostic = Diagnostic.Create(
            BackwardCompatibilityNotDeclaredRule,
            attribute.GetLocation(),
            typeName,
            version);
        context.ReportDiagnostic(diagnostic);
    }

    private static string ResolveAttributedTypeName(AttributeSyntax attribute)
    {
        SyntaxNode? current = attribute.Parent;

        while (current != null)
        {
            if (current is ClassDeclarationSyntax classDeclaration)
            {
                return classDeclaration.Identifier.Text;
            }

            if (current is RecordDeclarationSyntax recordDeclaration)
            {
                return recordDeclaration.Identifier.Text;
            }

            current = current.Parent;
        }

        return "contract";
    }
}
