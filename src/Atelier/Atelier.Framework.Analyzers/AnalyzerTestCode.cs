using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atelier.Framework.Analyzers;

internal static class AnalyzerTestCode
{
    private static readonly string[] TestMethodAttributeNames =
    {
        "Test",
        "Fact",
        "Theory",
        "TestMethod",
        "TestCase",
        "GeneratedTest"
    };

    public static bool IsTestCode(SyntaxNodeAnalysisContext context)
    {
        var namespaceSymbol = context.ContainingSymbol?.ContainingNamespace;
        if (namespaceSymbol != null)
        {
            var namespaceName = namespaceSymbol.ToDisplayString();
            if (namespaceName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
                || namespaceName.Contains(".Tests.", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var classDeclaration = context.Node.Ancestors()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault();

        if (classDeclaration != null)
        {
            var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration);
            if (classSymbol != null)
            {
                var hasTestAttribute = classSymbol.GetAttributes()
                    .Any(a => a.AttributeClass?.Name.Contains("Test", StringComparison.OrdinalIgnoreCase) == true);

                if (hasTestAttribute)
                {
                    return true;
                }
            }
        }

        var methodDeclaration = context.Node.Ancestors()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault();

        if (methodDeclaration != null)
        {
            var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration);
            if (methodSymbol != null)
            {
                var hasTestAttribute = methodSymbol.GetAttributes()
                    .Any(a => TestMethodAttributeNames.Any(ta =>
                        a.AttributeClass?.Name.Equals(ta + "Attribute", StringComparison.OrdinalIgnoreCase) == true
                        || a.AttributeClass?.Name.Equals(ta, StringComparison.OrdinalIgnoreCase) == true));

                if (hasTestAttribute)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
