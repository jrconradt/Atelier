using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atelier.Framework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MutatingApiScopeAnalyzer : DiagnosticAnalyzer
{
    public const string DIAGNOSTIC_ID = "ATELIER0750";

    private static readonly DiagnosticDescriptor WriteEffectApiWithoutWriteScopeRule = new DiagnosticDescriptor(
        DIAGNOSTIC_ID,
        "Write-effect API operation has no write-tier scope",
        "Operation '{0}' on [Api] class '{1}' declares the Write effect but has no write-tier authorization scope. Bind [ScopeResource(typeof(...))] on the class or an implemented interface, add an explicit [RequiresScope] on the method, or mark the method [AllowAnonymous] to expose it unprotected.",
        "Security",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "An [Api] operation whose declared effect is Write must carry a write-tier scope. The scope is derived from a [ScopeResource] binding on the declaring type or an implemented interface, or supplied explicitly with [RequiresScope]/[RequiresScopeContract]. A Write-effect operation with neither would ship as an unprotected write surface; it is refused at compile time. The effect is taken from a declared [OperationEffect] on the method or a type/interface-level default, not from the method name.",
        customTags: new[] { WellKnownDiagnosticTags.Compiler });

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(WriteEffectApiWithoutWriteScopeRule);

    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeApiClass, SyntaxKind.ClassDeclaration);
    }

    private static void AnalyzeApiClass(SyntaxNodeAnalysisContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration);

        if (classSymbol == null)
        {
            return;
        }

        if (!HasAttribute(classSymbol, "ApiAttribute"))
        {
            return;
        }

        if (HasAttribute(classSymbol, "AllowAnonymousAttribute"))
        {
            return;
        }

        if (TypeOrInterfacesHaveScopeResource(classSymbol))
        {
            return;
        }

        if (HasScopeAttribute(classSymbol))
        {
            return;
        }

        foreach (var member in classDeclaration.Members.OfType<MethodDeclarationSyntax>())
        {
            var methodSymbol = context.SemanticModel.GetDeclaredSymbol(member);
            if (methodSymbol == null
                || !IsExposedApiMethod(methodSymbol))
            {
                continue;
            }

            if (!ResolvesToWriteEffect(methodSymbol))
            {
                continue;
            }

            if (HasAttribute(methodSymbol, "AllowAnonymousAttribute"))
            {
                continue;
            }

            if (HasScopeAttribute(methodSymbol))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                WriteEffectApiWithoutWriteScopeRule,
                member.Identifier.GetLocation(),
                methodSymbol.Name,
                classSymbol.Name));
        }
    }

    private static bool ResolvesToWriteEffect(IMethodSymbol method)
    {
        var methodEffect = OperationEffectName(method);
        if (methodEffect != null)
        {
            return string.Equals(methodEffect, "Write", StringComparison.Ordinal);
        }

        return TypeOrInterfacesDeclareWriteEffect(method.ContainingType);
    }

    private static bool TypeOrInterfacesDeclareWriteEffect(INamedTypeSymbol type)
    {
        var typeEffect = OperationEffectName(type);
        if (typeEffect != null)
        {
            return string.Equals(typeEffect, "Write", StringComparison.Ordinal);
        }

        foreach (var interfaceSymbol in type.AllInterfaces)
        {
            var interfaceEffect = OperationEffectName(interfaceSymbol);
            if (interfaceEffect != null)
            {
                return string.Equals(interfaceEffect, "Write", StringComparison.Ordinal);
            }
        }

        return false;
    }

    private static string? OperationEffectName(ISymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass?.Name != "OperationEffectAttribute")
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length == 0)
            {
                return null;
            }

            return EnumMemberName(attribute.ConstructorArguments[0]);
        }

        return null;
    }

    private static string? EnumMemberName(TypedConstant argument)
    {
        if (argument.Type is not INamedTypeSymbol enumType
            || enumType.TypeKind != TypeKind.Enum)
        {
            return null;
        }

        foreach (var member in enumType.GetMembers())
        {
            if (member is IFieldSymbol field
                && field.HasConstantValue
                && Equals(field.ConstantValue, argument.Value))
            {
                return field.Name;
            }
        }

        return null;
    }

    private static bool TypeOrInterfacesHaveScopeResource(INamedTypeSymbol classSymbol)
    {
        if (HasAttribute(classSymbol, "ScopeResourceAttribute"))
        {
            return true;
        }

        foreach (var interfaceSymbol in classSymbol.AllInterfaces)
        {
            if (HasAttribute(interfaceSymbol, "ScopeResourceAttribute"))
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

    private static bool IsExposedApiMethod(IMethodSymbol method)
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
