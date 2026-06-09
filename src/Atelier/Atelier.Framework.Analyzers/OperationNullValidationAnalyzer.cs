using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atelier.Framework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class OperationNullValidationAnalyzer : DiagnosticAnalyzer
{
    public const string DIAGNOSTIC_ID = "ATELIER003";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DIAGNOSTIC_ID,
        "Missing null check in [Operation] method",
        "Operation method '{0}' does not null-check parameter '{1}'. Return Outcome.Failure(...) at the top.",
        "Atelier.Patterns",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "[Operation]-marked methods returning Outcome<T> must null-check every non-nullable reference parameter and return Outcome.Failure(...) on null.",
        customTags: new[] { WellKnownDiagnosticTags.Compiler });

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration);

        if (methodSymbol == null)
        {
            return;
        }

        if (!HasOperationAttribute(methodSymbol))
        {
            return;
        }

        var returnTypeStr = methodSymbol.ReturnType.ToDisplayString();
        if (!returnTypeStr.Contains("Outcome<"))
        {
            return;
        }

        var parametersNeedingValidation = methodSymbol.Parameters
            .Where(p => RequiresNullValidation(p))
            .ToList();

        if (parametersNeedingValidation.Count == 0)
        {
            return;
        }

        if (methodDeclaration.Body == null && methodDeclaration.ExpressionBody == null)
        {
            return;
        }

        foreach (var parameter in parametersNeedingValidation)
        {
            if (!HasNullCheck(methodDeclaration, parameter, context.SemanticModel))
            {
                var diagnostic = Diagnostic.Create(
                    Rule,
                    methodDeclaration.Identifier.GetLocation(),
                    methodSymbol.Name,
                    parameter.Name);

                context.ReportDiagnostic(diagnostic);

                break;
            }
        }
    }

    private static bool HasOperationAttribute(IMethodSymbol methodSymbol)
    {
        return methodSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.Name == "OperationAttribute");
    }

    private static bool RequiresNullValidation(IParameterSymbol parameter)
    {

        if (parameter.RefKind == RefKind.Out || parameter.RefKind == RefKind.Ref)
        {
            return false;
        }

        if (parameter.IsOptional || parameter.IsParams)
        {
            return false;
        }

        if (parameter.Type.IsValueType &&
            parameter.NullableAnnotation != NullableAnnotation.Annotated)
        {
            return false;
        }

        if (parameter.Type.ToDisplayString() == "System.Threading.CancellationToken")
        {
            return false;
        }

        if (parameter.NullableAnnotation == NullableAnnotation.Annotated)
        {
            return false;
        }

        return parameter.Type.SpecialType == SpecialType.System_String ||
               parameter.Type.TypeKind == TypeKind.Class ||
               parameter.Type.TypeKind == TypeKind.Interface ||
               parameter.Type.TypeKind == TypeKind.Delegate ||
               parameter.Type.TypeKind == TypeKind.Array;
    }

    private static bool HasNullCheck(
        MethodDeclarationSyntax methodDeclaration,
        IParameterSymbol parameter,
        SemanticModel semanticModel)
    {
        var body = methodDeclaration.Body;
        if (body == null)
        {
            return false;
        }

        foreach (var statement in body.Statements.Take(10))
        {
            if (statement is not IfStatementSyntax ifStatement)
            {
                continue;
            }

            if (ConditionNullChecksParameter(ifStatement.Condition, parameter, semanticModel)
                && BodyReturnsFailure(ifStatement.Statement, semanticModel))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ConditionNullChecksParameter(
        ExpressionSyntax condition,
        IParameterSymbol parameter,
        SemanticModel semanticModel)
    {
        foreach (var node in condition.DescendantNodesAndSelf())
        {
            if (node is BinaryExpressionSyntax binary
                && binary.IsKind(SyntaxKind.EqualsExpression))
            {
                if (binary.Right.IsKind(SyntaxKind.NullLiteralExpression)
                    && BindsToParameter(binary.Left, parameter, semanticModel))
                {
                    return true;
                }
                if (binary.Left.IsKind(SyntaxKind.NullLiteralExpression)
                    && BindsToParameter(binary.Right, parameter, semanticModel))
                {
                    return true;
                }
            }

            if (node is IsPatternExpressionSyntax isPattern
                && BindsToParameter(isPattern.Expression, parameter, semanticModel)
                && isPattern.Pattern is ConstantPatternSyntax constant
                && constant.Expression.IsKind(SyntaxKind.NullLiteralExpression))
            {
                return true;
            }
        }

        return false;
    }

    private static bool BodyReturnsFailure(StatementSyntax body, SemanticModel semanticModel)
    {
        foreach (var invocation in body.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is MemberAccessExpressionSyntax member
                && member.Name.Identifier.ValueText == "Failure")
            {
                var symbol = semanticModel.GetSymbolInfo(invocation).Symbol;
                if (symbol == null
                    || symbol.ContainingType?.Name == "Outcome")
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool BindsToParameter(
        ExpressionSyntax expression,
        IParameterSymbol parameter,
        SemanticModel semanticModel)
    {
        var symbol = semanticModel.GetSymbolInfo(expression).Symbol;
        return symbol is IParameterSymbol bound
            && SymbolEqualityComparer.Default.Equals(bound, parameter);
    }
}
