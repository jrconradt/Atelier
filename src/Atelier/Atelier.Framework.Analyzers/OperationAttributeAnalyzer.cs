using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atelier.Framework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OperationAttributeAnalyzer : DiagnosticAnalyzer
{
    private const string CATEGORY = "Atelier.Patterns";

    private static readonly DiagnosticDescriptor MissingOperationAttributeDiagnostic = new DiagnosticDescriptor(
        "ATELIER1404",
        "Public service method missing [Operation] attribute",
        "Method '{0}' in service '{1}' is public but missing [Operation] attribute. Add [Operation(\"{0}\")] to enable telemetry, authorization, and operation tracking.",
        CATEGORY,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "All public methods in service classes should be marked with [Operation] attribute for proper telemetry, authorization, and operation tracking.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(MissingOperationAttributeDiagnostic);

    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethodDeclaration, SyntaxKind.MethodDeclaration);
    }

    private void AnalyzeMethodDeclaration(SyntaxNodeAnalysisContext context)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration);

        if (methodSymbol == null)
        {
            return;
        }

        if (methodSymbol.DeclaredAccessibility != Accessibility.Public)
        {
            return;
        }

        if (methodSymbol.IsStatic)
        {
            return;
        }

        var containingType = methodSymbol.ContainingType;
        if (containingType == null || containingType.TypeKind != TypeKind.Class)
        {
            return;
        }

        if (!IsServiceClass(containingType))
        {
            return;
        }

        if (!IsOutcomeShaped(methodSymbol.ReturnType))
        {
            return;
        }

        if (AnalyzerTestCode.IsTestCode(context))
        {
            return;
        }

        if (IsConstructorOrPropertyAccessor(methodSymbol))
        {
            return;
        }

        if (methodSymbol.IsOverride)
        {
            return;
        }



        if (methodSymbol.ExplicitInterfaceImplementations.Length > 0)
        {
            return;
        }
        if (IsImplicitInterfaceImplementation(methodSymbol))
        {
            return;
        }

        switch (methodSymbol.Name)
        {
            case "Dispose":
            case "DisposeAsync":
            case "ToString":
            case "Equals":
            case "GetHashCode":
            case "Configure":
                return;
        }

        var hasOperationAttribute = methodSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.Name == "OperationAttribute");

        if (!hasOperationAttribute)
        {
            var diagnostic = Diagnostic.Create(
                MissingOperationAttributeDiagnostic,
                methodDeclaration.Identifier.GetLocation(),
                methodSymbol.Name,
                containingType.Name);
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static bool IsOutcomeShaped(ITypeSymbol returnType)
    {
        if (returnType is not INamedTypeSymbol named)
        {
            return false;
        }

        if (named.Name == "Outcome")
        {
            return true;
        }

        if ((named.Name == "Task" || named.Name == "ValueTask")
            && named.IsGenericType)
        {
            return named.TypeArguments.FirstOrDefault() is INamedTypeSymbol inner
                && inner.Name == "Outcome";
        }

        return false;
    }

    private static bool IsServiceClass(INamedTypeSymbol classSymbol)
    {
        if (classSymbol.Name.EndsWith("Service", StringComparison.Ordinal))
        {
            return true;
        }

        return classSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.Name == "InfrastructureAttribute");
    }

    private static bool IsConstructorOrPropertyAccessor(IMethodSymbol method)
    {
        return method.MethodKind == MethodKind.Constructor ||
               method.MethodKind == MethodKind.PropertyGet ||
               method.MethodKind == MethodKind.PropertySet ||
               method.MethodKind == MethodKind.StaticConstructor;
    }

    private static bool IsImplicitInterfaceImplementation(IMethodSymbol method)
    {
        var containingType = method.ContainingType;
        if (containingType == null)
        {
            return false;
        }
        foreach (var iface in containingType.AllInterfaces)
        {
            foreach (var ifaceMember in iface.GetMembers())
            {
                if (ifaceMember is not IMethodSymbol ifaceMethod)
                {
                    continue;
                }
                var impl = containingType.FindImplementationForInterfaceMember(ifaceMethod);
                if (SymbolEqualityComparer.Default.Equals(impl, method))
                {
                    return true;
                }
            }
        }
        return false;
    }
}
