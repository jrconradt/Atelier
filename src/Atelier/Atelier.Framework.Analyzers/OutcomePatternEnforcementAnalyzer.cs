using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atelier.Framework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OutcomePatternEnforcementAnalyzer : DiagnosticAnalyzer
{
    private const string CATEGORY = "Atelier.Patterns";

    private static readonly DiagnosticDescriptor ThrowInOperationDiagnostic = new DiagnosticDescriptor(
        "ATELIER1000",
        "Operation throws exception instead of returning Outcome.Failure()",
        "Method '{0}' with [Operation] attribute throws '{1}' instead of returning Outcome.Failure(). Use Outcome pattern for flow control.",
        CATEGORY,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Operations should return Outcome<T>.Failure() instead of throwing exceptions for flow control. " +
                     "Exceptions should only be used for truly exceptional circumstances (programmer errors, system failures).");

    private static readonly DiagnosticDescriptor ThrowInServiceDiagnostic = new DiagnosticDescriptor(
        "ATELIER1001",
        "Service method throws exception instead of returning Outcome.Failure()",
        "Public method '{0}' in service class throws '{1}'. Consider returning Outcome<T>.Failure() instead.",
        CATEGORY,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Service methods should prefer Outcome<T> pattern over exception throwing for business logic errors.");

    private static readonly DiagnosticDescriptor AcceptableArgumentExceptionDiagnostic = new DiagnosticDescriptor(
        "ATELIER1002",
        "ArgumentException in operation - consider validation before operation call",
        "Method '{0}' throws ArgumentException. Consider validating arguments before calling the operation.",
        CATEGORY,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "While ArgumentException is acceptable for input validation, consider validating before the operation call.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            ThrowInOperationDiagnostic,
            ThrowInServiceDiagnostic,
            AcceptableArgumentExceptionDiagnostic);
    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeThrowStatement, SyntaxKind.ThrowStatement);
        context.RegisterSyntaxNodeAction(AnalyzeThrowExpression, SyntaxKind.ThrowExpression);
    }

    private void AnalyzeThrowStatement(SyntaxNodeAnalysisContext context)
    {
        var throwStatement = (ThrowStatementSyntax)context.Node;
        AnalyzeThrow(context, throwStatement.Expression, throwStatement.GetLocation());
    }

    private void AnalyzeThrowExpression(SyntaxNodeAnalysisContext context)
    {
        var throwExpression = (ThrowExpressionSyntax)context.Node;
        AnalyzeThrow(context, throwExpression.Expression, throwExpression.GetLocation());
    }

    private void AnalyzeThrow(SyntaxNodeAnalysisContext context, ExpressionSyntax? expression, Location location)
    {
        if (expression == null)
        {
            return;
        }

        var containingMethod = expression.Ancestors()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault();

        if (containingMethod == null)
        {
            return;
        }

        var enclosingFunction = expression.Ancestors()
            .FirstOrDefault(a =>
                a is SimpleLambdaExpressionSyntax
                || a is ParenthesizedLambdaExpressionSyntax
                || a is AnonymousMethodExpressionSyntax
                || a is LocalFunctionStatementSyntax
                || a is MethodDeclarationSyntax);
        if (enclosingFunction is not MethodDeclarationSyntax)
        {
            return;
        }

        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(containingMethod);
        if (methodSymbol == null)
        {
            return;
        }

        var exceptionType = GetExceptionType(context, expression);
        var exceptionTypeName = exceptionType?.Name ?? "Exception";

        var hasOperationAttribute = methodSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.Name == "OperationAttribute");

        if (hasOperationAttribute)
        {

            if (IsArgumentException(exceptionType))
            {
                var diagnostic = Diagnostic.Create(
                    AcceptableArgumentExceptionDiagnostic,
                    location,
                    methodSymbol.Name);
                context.ReportDiagnostic(diagnostic);
            }
            else
            {
                var diagnostic = Diagnostic.Create(
                    ThrowInOperationDiagnostic,
                    location,
                    methodSymbol.Name,
                    exceptionTypeName);
                context.ReportDiagnostic(diagnostic);
            }
            return;
        }

        var containingClass = methodSymbol.ContainingType;
        if (containingClass != null && IsServiceClass(containingClass))
        {

            if (methodSymbol.DeclaredAccessibility == Accessibility.Public ||
                methodSymbol.DeclaredAccessibility == Accessibility.Internal)
            {

                if (IsArgumentException(exceptionType))
                {
                    return;
                }

                if (ReturnsOutcome(methodSymbol))
                {
                    return;
                }

                var diagnostic = Diagnostic.Create(
                    ThrowInServiceDiagnostic,
                    location,
                    methodSymbol.Name,
                    exceptionTypeName);
                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    private static INamedTypeSymbol? GetExceptionType(SyntaxNodeAnalysisContext context, ExpressionSyntax expression)
    {

        if (expression is ObjectCreationExpressionSyntax objectCreation)
        {
            var typeInfo = context.SemanticModel.GetTypeInfo(objectCreation);
            return typeInfo.Type as INamedTypeSymbol;
        }

        var symbolInfo = context.SemanticModel.GetSymbolInfo(expression);
        if (symbolInfo.Symbol is ILocalSymbol localSymbol)
        {
            return localSymbol.Type as INamedTypeSymbol;
        }

        return null;
    }

    private static bool IsArgumentException(INamedTypeSymbol? exceptionType)
    {
        if (exceptionType == null)
        {
            return false;
        }

        var typeName = exceptionType.Name;
        return typeName == "ArgumentException" ||
               typeName == "ArgumentNullException" ||
               typeName == "ArgumentOutOfRangeException";
    }

    private static bool IsServiceClass(INamedTypeSymbol classSymbol)
    {

        var hasInfrastructureAttribute = classSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.Name == "InfrastructureAttribute");

        if (hasInfrastructureAttribute)
        {
            return true;
        }

        return classSymbol.Name.EndsWith("Service", StringComparison.OrdinalIgnoreCase) ||
               classSymbol.Name.EndsWith("Offering", StringComparison.OrdinalIgnoreCase) ||
               classSymbol.Name.EndsWith("Repository", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ReturnsOutcome(IMethodSymbol methodSymbol)
    {
        var returnType = methodSymbol.ReturnType;

        if (returnType is INamedTypeSymbol namedType)
        {
            if (namedType.Name == "Outcome" && namedType.IsGenericType)
            {
                return true;
            }

            if (namedType.Name == "Task" && namedType.IsGenericType)
            {
                var typeArg = namedType.TypeArguments.FirstOrDefault();
                if (typeArg is INamedTypeSymbol innerType &&
                    innerType.Name == "Outcome" &&
                    innerType.IsGenericType)
                {
                    return true;
                }
            }

            if (namedType.Name == "ValueTask" && namedType.IsGenericType)
            {
                var typeArg = namedType.TypeArguments.FirstOrDefault();
                if (typeArg is INamedTypeSymbol innerType &&
                    innerType.Name == "Outcome" &&
                    innerType.IsGenericType)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
