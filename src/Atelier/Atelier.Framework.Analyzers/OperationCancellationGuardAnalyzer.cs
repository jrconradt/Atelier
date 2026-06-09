using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atelier.Framework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OperationCancellationGuardAnalyzer : DiagnosticAnalyzer
{
    public const string DIAGNOSTIC_ID = "ATELIER1310";
    private const string CATEGORY = "Atelier.Patterns";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DIAGNOSTIC_ID,
        "[Operation] method missing CancellationToken guard at entry",
        "[Operation] method '{0}' takes a CancellationToken but does not check it before doing work. " +
        "Add 'if (cancellationToken.IsCancellationRequested) return Outcome.Failure(...);' or " +
        "'cancellationToken.ThrowIfCancellationRequested();' at method entry.",
        CATEGORY,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Public [Operation] methods that receive a CancellationToken must honor it eagerly. " +
                     "Check IsCancellationRequested (or ThrowIfCancellationRequested) before performing any other work.");


    private const int MAX_LEADING_STATEMENTS_TO_INSPECT = 3;

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

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

    private static void AnalyzeMethodDeclaration(SyntaxNodeAnalysisContext context)
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

        var ctParameter = methodSymbol.Parameters.FirstOrDefault(IsCancellationTokenParameter);
        if (ctParameter == null)
        {
            return;
        }

        if (methodDeclaration.Body == null && methodDeclaration.ExpressionBody == null)
        {
            return;
        }


        if (methodDeclaration.Body == null)
        {

            ReportMissingGuard(context, methodDeclaration, methodSymbol);
            return;
        }

        if (HasLeadingCancellationGuard(methodDeclaration.Body, ctParameter, context.SemanticModel))
        {
            return;
        }

        ReportMissingGuard(context, methodDeclaration, methodSymbol);
    }

    private static void ReportMissingGuard(
        SyntaxNodeAnalysisContext context,
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol)
    {
        var diagnostic = Diagnostic.Create(
            Rule,
            methodDeclaration.Identifier.GetLocation(),
            methodSymbol.Name);
        context.ReportDiagnostic(diagnostic);
    }

    private static bool HasOperationAttribute(IMethodSymbol methodSymbol)
    {
        return methodSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.Name is "OperationAttribute" or "Operation");
    }

    private static bool IsCancellationTokenParameter(IParameterSymbol parameter)
    {
        var type = parameter.Type;

        if (type is INamedTypeSymbol named &&
            named.IsGenericType &&
            named.ConstructedFrom?.SpecialType == SpecialType.System_Nullable_T &&
            named.TypeArguments.Length == 1)
        {
            type = named.TypeArguments[0];
        }

        return type.Name == "CancellationToken" &&
               type.ContainingNamespace?.ToDisplayString() == "System.Threading";
    }

    private static bool HasLeadingCancellationGuard(
        BlockSyntax body,
        IParameterSymbol ctParameter,
        SemanticModel semanticModel)
    {
        var inspected = 0;

        foreach (var statement in body.Statements)
        {
            if (inspected >= MAX_LEADING_STATEMENTS_TO_INSPECT)
            {
                return false;
            }

            inspected++;



            if (statement is ExpressionStatementSyntax expressionStatement &&
                expressionStatement.Expression is InvocationExpressionSyntax invocation &&
                IsInvocationOnCtParameter(invocation, ctParameter, semanticModel))
            {
                return true;
            }

            if (statement is IfStatementSyntax ifStatement &&
                ConditionReferencesCtIsCancellationRequested(ifStatement.Condition, ctParameter, semanticModel) &&
                IsImmediateExit(ifStatement.Statement))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInvocationOnCtParameter(
        InvocationExpressionSyntax invocation,
        IParameterSymbol ctParameter,
        SemanticModel semanticModel)
    {

        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            return IsCtParameterReference(memberAccess.Expression, ctParameter, semanticModel);
        }

        if (invocation.ArgumentList.Arguments.Count > 0)
        {
            var firstArg = invocation.ArgumentList.Arguments[0].Expression;
            if (IsCtParameterReference(firstArg, ctParameter, semanticModel))
            {

                if (invocation.Expression is MemberAccessExpressionSyntax helperAccess)
                {
                    var name = helperAccess.Name.Identifier.Text;
                    if (name.Contains("Cancel"))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool ConditionReferencesCtIsCancellationRequested(
        ExpressionSyntax condition,
        IParameterSymbol ctParameter,
        SemanticModel semanticModel)
    {
        foreach (var memberAccess in condition.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>())
        {
            if (memberAccess.Name.Identifier.Text == "IsCancellationRequested" &&
                IsCtParameterReference(memberAccess.Expression, ctParameter, semanticModel))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsCtParameterReference(
        ExpressionSyntax expression,
        IParameterSymbol ctParameter,
        SemanticModel semanticModel)
    {

        if (expression is MemberAccessExpressionSyntax valueAccess &&
            valueAccess.Name.Identifier.Text == "Value")
        {
            expression = valueAccess.Expression;
        }

        if (expression is IdentifierNameSyntax identifier &&
            identifier.Identifier.Text == ctParameter.Name)
        {
            return true;
        }

        var symbol = semanticModel.GetSymbolInfo(expression).Symbol;
        return SymbolEqualityComparer.Default.Equals(symbol, ctParameter);
    }

    private static bool IsImmediateExit(StatementSyntax statement)
    {


        switch (statement)
        {
            case ReturnStatementSyntax:
            case ThrowStatementSyntax:
                return true;
            case BlockSyntax block when block.Statements.Count >= 1:
                return block.Statements[0] is ReturnStatementSyntax or ThrowStatementSyntax;
            default:
                return false;
        }
    }
}
