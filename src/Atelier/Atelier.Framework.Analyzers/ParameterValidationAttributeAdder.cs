using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atelier.Framework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ParameterValidationAttributeAdder : DiagnosticAnalyzer
{
    public const string DIAGNOSTIC_ID = "ATELIER004_FIX";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DIAGNOSTIC_ID,
        "Add Parameter Validation",
        "Method '{0}' needs [Validated] attribute for parameter validation",
        "Validation",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Automatically add [Validated] attribute to methods requiring parameter validation.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethodDeclaration, SyntaxKind.MethodDeclaration);
    }

    private void AnalyzeMethodDeclaration(SyntaxNodeAnalysisContext context)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration);

        if (methodSymbol == null || methodSymbol.IsStatic)
        {
            return;
        }

        if (IsExcludedFromValidation(methodSymbol))
        {
            return;
        }

        var parameters = methodSymbol.Parameters
            .Where(p => RequiresValidation(p))
            .ToList();

        if (parameters.Any() && !HasValidationAttribute(methodSymbol) && !HasValidationCode(methodDeclaration))
        {
            var diagnostic = Diagnostic.Create(
                Rule,
                methodDeclaration.Identifier.GetLocation(),
                methodSymbol.Name);
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static bool IsExcludedFromValidation(IMethodSymbol methodSymbol)
    {
        return methodSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.Name == "ExcludeFromValidationAttribute");
    }

    private static bool RequiresValidation(IParameterSymbol parameter)
    {
        var type = parameter.Type;
        return type.IsReferenceType && type.Name != "String";
    }

    private static bool HasValidationAttribute(IMethodSymbol methodSymbol)
    {
        return methodSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.Name == "ValidatedAttribute");
    }

    private static bool HasValidationCode(MethodDeclarationSyntax methodDeclaration)
    {
        if (methodDeclaration.Body == null)
        {
            return false;
        }

        return methodDeclaration.Body.Statements
            .OfType<ExpressionStatementSyntax>()
            .Select(ess => ess.Expression as InvocationExpressionSyntax)
            .Where(ies => ies != null)
            .Select(ies => ies?.Expression as MemberAccessExpressionSyntax)
            .Where(maes => maes != null)
            .Any(maes => maes?.Name.Identifier.Text == "ThrowIfNull" ||
                        maes?.Name.Identifier.Text == "Validate");
    }
}
