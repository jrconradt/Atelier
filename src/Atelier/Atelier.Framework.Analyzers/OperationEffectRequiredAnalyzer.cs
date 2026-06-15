using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atelier.Framework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OperationEffectRequiredAnalyzer : DiagnosticAnalyzer
{
    public const string DIAGNOSTIC_ID = "ATELIER0751";

    private static readonly DiagnosticDescriptor UndeclaredOperationEffectRule = new DiagnosticDescriptor(
        DIAGNOSTIC_ID,
        "Operation on a [ScopeResource] type does not declare its authorization effect",
        "Operation '{0}' on [ScopeResource] type '{1}' must declare [OperationEffect(EffectKind.Read|Write)] so its authorization tier is explicit. The method name no longer decides it.",
        "Security",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Scope-tier derivation reads the READ or WRITE const from the [ScopeResource]-bound type to authorize an operation. The read-versus-write tier is taken from a declared [OperationEffect], not from the method name. An exposed operation on a [ScopeResource] target that declares no effect on the method, no type-level or interface-level [OperationEffect] default, and no explicit [RequiresScope]/[RequiresScopeContract] has no determinable tier; it is refused at compile time so a deceptively named method cannot resolve to the wrong tier at runtime.",
        customTags: new[] { WellKnownDiagnosticTags.Compiler });

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(UndeclaredOperationEffectRule);

    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(
            AnalyzeType,
            SyntaxKind.ClassDeclaration,
            SyntaxKind.InterfaceDeclaration);
    }

    private static void AnalyzeType(SyntaxNodeAnalysisContext context)
    {
        var typeDeclaration = (TypeDeclarationSyntax)context.Node;
        var typeSymbol = context.SemanticModel.GetDeclaredSymbol(typeDeclaration);
        if (typeSymbol == null)
        {
            return;
        }

        if (!TypeOrInterfacesHaveScopeResource(typeSymbol))
        {
            return;
        }

        foreach (var member in typeDeclaration.Members.OfType<MethodDeclarationSyntax>())
        {
            var methodSymbol = context.SemanticModel.GetDeclaredSymbol(member);
            if (methodSymbol == null
                || !IsExposedOperation(methodSymbol))
            {
                continue;
            }

            if (HasDeterminableEffect(methodSymbol, typeSymbol))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                UndeclaredOperationEffectRule,
                member.Identifier.GetLocation(),
                methodSymbol.Name,
                typeSymbol.Name));
        }
    }

    private static bool HasDeterminableEffect(IMethodSymbol method,
                                              INamedTypeSymbol containingType)
    {
        if (HasAttribute(method, "OperationEffectAttribute"))
        {
            return true;
        }

        if (HasScopeAttribute(method))
        {
            return true;
        }

        if (TypeOrInterfacesHaveOperationEffect(containingType))
        {
            return true;
        }

        if (HasScopeAttribute(containingType))
        {
            return true;
        }

        return false;
    }

    private static bool TypeOrInterfacesHaveScopeResource(INamedTypeSymbol typeSymbol)
    {
        if (HasAttribute(typeSymbol, "ScopeResourceAttribute"))
        {
            return true;
        }

        foreach (var interfaceSymbol in typeSymbol.AllInterfaces)
        {
            if (HasAttribute(interfaceSymbol, "ScopeResourceAttribute"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TypeOrInterfacesHaveOperationEffect(INamedTypeSymbol typeSymbol)
    {
        if (HasAttribute(typeSymbol, "OperationEffectAttribute"))
        {
            return true;
        }

        foreach (var interfaceSymbol in typeSymbol.AllInterfaces)
        {
            if (HasAttribute(interfaceSymbol, "OperationEffectAttribute"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasScopeAttribute(ISymbol symbol)
    {
        return HasAttribute(symbol, "RequiresScopeAttribute")
            || HasAttribute(symbol, "RequiresScopeContractAttribute");
    }

    private static bool HasAttribute(ISymbol symbol,
                                     string attributeName)
    {
        return symbol.GetAttributes()
            .Any(a => a.AttributeClass?.Name == attributeName);
    }

    private static bool IsExposedOperation(IMethodSymbol method)
    {
        if (method.DeclaredAccessibility != Accessibility.Public)
        {
            return false;
        }

        if (method.MethodKind != MethodKind.Ordinary)
        {
            return false;
        }

        if (method.IsStatic)
        {
            return false;
        }

        return true;
    }
}
