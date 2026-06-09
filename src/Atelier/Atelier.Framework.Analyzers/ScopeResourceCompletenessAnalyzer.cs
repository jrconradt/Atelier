using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atelier.Framework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ScopeResourceCompletenessAnalyzer : DiagnosticAnalyzer
{
    public const string DIAGNOSTIC_ID = "ATELIER0752";

    private static readonly DiagnosticDescriptor IncompleteScopeResourceRule = new DiagnosticDescriptor(
        DIAGNOSTIC_ID,
        "[ScopeResource] target does not expose both READ and WRITE scope constants",
        "The scope-pair type '{0}' bound by [ScopeResource] is missing the string const(s): {1}. A [ScopeResource(typeof(T))] target must declare both 'public const string READ' and 'public const string WRITE' so the read and write tier scopes are derivable.",
        "Security",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Scope-tier derivation reads the READ or WRITE const from the [ScopeResource]-bound type to authorize an operation. If either const is absent or blank, a bound operation resolves to no required scope and would admit any principal. A scope-pair type that does not expose both string consts named READ and WRITE is refused at compile time.",
        customTags: new[] { WellKnownDiagnosticTags.Compiler });

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(IncompleteScopeResourceRule);

    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeAttribute, SyntaxKind.Attribute);
    }

    private static void AnalyzeAttribute(SyntaxNodeAnalysisContext context)
    {
        var attribute = (AttributeSyntax)context.Node;
        var attributeSymbol = context.SemanticModel.GetSymbolInfo(attribute).Symbol as IMethodSymbol;
        if (attributeSymbol?.ContainingType?.Name != "ScopeResourceAttribute")
        {
            return;
        }

        var arguments = attribute.ArgumentList?.Arguments;
        if (arguments == null
            || arguments.Value.Count == 0)
        {
            return;
        }

        var firstArgument = arguments.Value[0].Expression;
        if (firstArgument is not TypeOfExpressionSyntax typeOf)
        {
            return;
        }

        var scopePairType = context.SemanticModel.GetTypeInfo(typeOf.Type).Type as INamedTypeSymbol;
        if (scopePairType == null)
        {
            return;
        }

        var hasRead = HasStringConst(scopePairType, "READ");
        var hasWrite = HasStringConst(scopePairType, "WRITE");

        if (hasRead
            && hasWrite)
        {
            return;
        }

        var missing = MissingDescription(hasRead, hasWrite);

        context.ReportDiagnostic(Diagnostic.Create(
            IncompleteScopeResourceRule,
            typeOf.Type.GetLocation(),
            scopePairType.Name,
            missing));
    }

    private static string MissingDescription(bool hasRead,
                                             bool hasWrite)
    {
        if (!hasRead
            && !hasWrite)
        {
            return "READ, WRITE";
        }

        if (!hasRead)
        {
            return "READ";
        }

        return "WRITE";
    }

    private static bool HasStringConst(INamedTypeSymbol type,
                                       string constName)
    {
        foreach (var member in type.GetMembers(constName))
        {
            if (member is IFieldSymbol field
                && field.IsConst
                && field.Type.SpecialType == SpecialType.System_String)
            {
                return true;
            }
        }

        return false;
    }
}
