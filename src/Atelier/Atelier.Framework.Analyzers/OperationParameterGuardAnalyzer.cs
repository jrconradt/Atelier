using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atelier.Framework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OperationParameterGuardAnalyzer : DiagnosticAnalyzer
{
    public const string DIAGNOSTIC_ID = "ATELIER0010";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DIAGNOSTIC_ID,
        "Missing null guard on [Operation] non-nullable reference parameter",
        "[Operation] method '{0}': non-nullable reference parameter '{1}' must be null-checked at method entry, returning Outcome.Failure(...) on null",
        "Atelier.Patterns",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Every [Operation]-attributed method must null-check each non-nullable reference parameter at method entry, returning Outcome.Failure(...) (or equivalent ArgumentNullException) before any dereference.",
        customTags: new[] { WellKnownDiagnosticTags.Compiler });

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

        if (methodDeclaration.Body == null && methodDeclaration.ExpressionBody == null)
        {
            return;
        }

        if (IsThrowOnlyExpressionBody(methodDeclaration))
        {
            return;
        }
        if (IsThrowOnlyBlockBody(methodDeclaration))
        {
            return;
        }

        var parametersNeedingGuard = methodSymbol.Parameters
            .Where(RequiresNullGuard)
            .ToList();

        if (parametersNeedingGuard.Count == 0)
        {
            return;
        }

        var paramNames = new HashSet<string>(parametersNeedingGuard.Select(p => p.Name));

        if (methodDeclaration.Body != null)
        {
            AnalyzeBlockBody(context, methodDeclaration, methodSymbol, parametersNeedingGuard, paramNames);
        }
        else if (methodDeclaration.ExpressionBody != null)
        {
            var expr = methodDeclaration.ExpressionBody.Expression;
            foreach (var p in parametersNeedingGuard)
            {
                if (NodeDereferencesParam(expr, p.Name))
                {
                    ReportMissingGuard(context, methodDeclaration, methodSymbol, p);
                }
            }
        }
    }

    private static void AnalyzeBlockBody(
        SyntaxNodeAnalysisContext context,
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol,
        List<IParameterSymbol> parametersNeedingGuard,
        HashSet<string> paramNames)
    {
        var body = methodDeclaration.Body!;
        var guarded = new HashSet<string>();
        var reported = new HashSet<string>();

        AnalyzeStatements(
            body.Statements,
            paramNames,
            parametersNeedingGuard,
            guarded,
            reported,
            context,
            methodDeclaration,
            methodSymbol);
    }

    private static void AnalyzeStatements(
        IEnumerable<StatementSyntax> statements,
        HashSet<string> paramNames,
        List<IParameterSymbol> parametersNeedingGuard,
        HashSet<string> guarded,
        HashSet<string> reported,
        SyntaxNodeAnalysisContext context,
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol)
    {
        var work = new Stack<(StatementSyntax statement, bool derefCheck)>();
        PushStatementsReversed(work, statements);

        while (work.Count > 0)
        {
            var (statement, derefCheck) = work.Pop();

            if (derefCheck)
            {
                foreach (var p in parametersNeedingGuard)
                {
                    if (guarded.Contains(p.Name) || reported.Contains(p.Name))
                    {
                        continue;
                    }

                    if (StatementDereferencesParam(statement, p.Name))
                    {
                        ReportMissingGuard(context, methodDeclaration, methodSymbol, p);
                        reported.Add(p.Name);
                    }
                }

                continue;
            }

            var guardedByThisStmt = TryExtractGuardedParams(statement, paramNames);
            foreach (var name in guardedByThisStmt)
            {
                guarded.Add(name);
            }

            work.Push((statement, true));

            var nestedLists = new List<IEnumerable<StatementSyntax>>();
            foreach (var nested in EnumerateNestedStatementLists(statement))
            {
                nestedLists.Add(nested);
            }

            for (var i = nestedLists.Count - 1; i >= 0; i--)
            {
                PushStatementsReversed(work, nestedLists[i]);
            }
        }
    }

    private static void PushStatementsReversed(
        Stack<(StatementSyntax statement, bool derefCheck)> work,
        IEnumerable<StatementSyntax> statements)
    {
        var buffer = new List<StatementSyntax>();
        foreach (var statement in statements)
        {
            buffer.Add(statement);
        }

        for (var i = buffer.Count - 1; i >= 0; i--)
        {
            work.Push((buffer[i], false));
        }
    }

        private static IEnumerable<IEnumerable<StatementSyntax>> EnumerateNestedStatementLists(StatementSyntax statement)
    {
        switch (statement)
        {
            case TryStatementSyntax tryStmt:
                yield return tryStmt.Block.Statements;

                break;
            case CheckedStatementSyntax checkedStmt:
                yield return checkedStmt.Block.Statements;
                break;
            case UnsafeStatementSyntax unsafeStmt:
                yield return unsafeStmt.Block.Statements;
                break;
            case LockStatementSyntax lockStmt when lockStmt.Statement is BlockSyntax lockBlock:
                yield return lockBlock.Statements;
                break;
            case BlockSyntax block:
                yield return block.Statements;
                break;
        }
    }

    private static void ReportMissingGuard(
        SyntaxNodeAnalysisContext context,
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol,
        IParameterSymbol parameter)
    {
        var diagnostic = Diagnostic.Create(
            Rule,
            methodDeclaration.Identifier.GetLocation(),
            methodSymbol.Name,
            parameter.Name);
        context.ReportDiagnostic(diagnostic);
    }

    private static bool HasOperationAttribute(IMethodSymbol methodSymbol)
    {
        return methodSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.Name is "OperationAttribute" or "Operation");
    }

    private static bool RequiresNullGuard(IParameterSymbol parameter)
    {
        if (parameter.RefKind == RefKind.Out)
        {
            return false;
        }

        if (parameter.IsParams)
        {
            return false;
        }

        if (parameter.Type.ToDisplayString() == "System.Threading.CancellationToken")
        {
            return false;
        }

        if (!parameter.Type.IsReferenceType || parameter.Type.IsValueType)
        {
            return false;
        }

        if (parameter.NullableAnnotation != NullableAnnotation.NotAnnotated)
        {
            return false;
        }


        if (parameter.IsOptional
            && parameter.HasExplicitDefaultValue)
        {
            return false;
        }

        return parameter.Type.SpecialType == SpecialType.System_String ||
               parameter.Type.TypeKind == TypeKind.Class ||
               parameter.Type.TypeKind == TypeKind.Interface ||
               parameter.Type.TypeKind == TypeKind.Delegate ||
               parameter.Type.TypeKind == TypeKind.Array ||
               parameter.Type.TypeKind == TypeKind.TypeParameter;
    }

    private static HashSet<string> TryExtractGuardedParams(StatementSyntax statement, HashSet<string> paramNames)
    {
        var result = new HashSet<string>();

        if (statement is IfStatementSyntax ifStmt)
        {

            if (!IsTerminatingBody(ifStmt.Statement))
            {
                return result;
            }

            foreach (var name in ExtractNullCheckedIdentifiers(ifStmt.Condition))
            {
                if (paramNames.Contains(name))
                {
                    result.Add(name);
                }
            }

            foreach (var name in ExtractIsNullOrCheckedIdentifiers(ifStmt.Condition))
            {
                if (paramNames.Contains(name))
                {
                    result.Add(name);
                }
            }
        }

        if (statement is ExpressionStatementSyntax exprStmt)
        {

            if (exprStmt.Expression is InvocationExpressionSyntax invocation &&
                invocation.Expression is MemberAccessExpressionSyntax mae &&
                mae.Name.Identifier.ValueText == "ThrowIfNull")
            {
                foreach (var arg in invocation.ArgumentList.Arguments)
                {
                    if (arg.Expression is IdentifierNameSyntax id && paramNames.Contains(id.Identifier.ValueText))
                    {
                        result.Add(id.Identifier.ValueText);
                    }
                }
            }

            if (exprStmt.Expression is AssignmentExpressionSyntax assignment &&
                assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression) &&
                assignment.Left is IdentifierNameSyntax leftId &&
                paramNames.Contains(leftId.Identifier.ValueText) &&
                assignment.Right is ThrowExpressionSyntax)
            {
                result.Add(leftId.Identifier.ValueText);
            }
        }

        return result;
    }

    private static string? ExtractNullCheckedIdentifier(ExpressionSyntax condition)
    {

        if (condition is IsPatternExpressionSyntax isPattern &&
            isPattern.Pattern is ConstantPatternSyntax cp &&
            cp.Expression.IsKind(SyntaxKind.NullLiteralExpression) &&
            isPattern.Expression is IdentifierNameSyntax isPatternId)
        {
            return isPatternId.Identifier.ValueText;
        }

        if (condition is BinaryExpressionSyntax binary &&
            binary.IsKind(SyntaxKind.EqualsExpression))
        {
            if (binary.Left is IdentifierNameSyntax leftId &&
                binary.Right.IsKind(SyntaxKind.NullLiteralExpression))
            {
                return leftId.Identifier.ValueText;
            }
            if (binary.Right is IdentifierNameSyntax rightId &&
                binary.Left.IsKind(SyntaxKind.NullLiteralExpression))
            {
                return rightId.Identifier.ValueText;
            }
        }

        return null;
    }

    private static IEnumerable<string> ExtractNullCheckedIdentifiers(ExpressionSyntax condition)
    {
        var result = new List<string>();
        var work = new Stack<ExpressionSyntax>();
        work.Push(condition);

        while (work.Count > 0)
        {
            var current = work.Pop();

            if (current is ParenthesizedExpressionSyntax paren)
            {
                work.Push(paren.Expression);
                continue;
            }

            if (current is BinaryExpressionSyntax binary
                && binary.IsKind(SyntaxKind.LogicalOrExpression))
            {
                work.Push(binary.Left);
                work.Push(binary.Right);
                continue;
            }

            var single = ExtractNullCheckedIdentifier(current);
            if (single != null)
            {
                result.Add(single);
            }
        }

        return result;
    }

    private static IEnumerable<string> ExtractIsNullOrCheckedIdentifiers(ExpressionSyntax condition)
    {
        foreach (var node in condition.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            if (node.Expression is MemberAccessExpressionSyntax mae &&
                (mae.Name.Identifier.ValueText == "IsNullOrEmpty" ||
                 mae.Name.Identifier.ValueText == "IsNullOrWhiteSpace"))
            {
                foreach (var arg in node.ArgumentList.Arguments)
                {
                    if (arg.Expression is IdentifierNameSyntax id)
                    {
                        yield return id.Identifier.ValueText;
                    }
                }
            }
        }
    }

    private static bool IsTerminatingBody(StatementSyntax stmt)
    {
        if (stmt is ReturnStatementSyntax || stmt is ThrowStatementSyntax)
        {
            return true;
        }

        if (stmt is BlockSyntax block)
        {
            foreach (var inner in block.Statements)
            {
                if (inner is ReturnStatementSyntax || inner is ThrowStatementSyntax)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool StatementDereferencesParam(StatementSyntax statement, string paramName)
    {
        return NodeDereferencesParam(statement, paramName);
    }

    private static bool NodeDereferencesParam(SyntaxNode node, string paramName)
    {
        foreach (var descendant in node.DescendantNodesAndSelf())
        {

            if (descendant is MemberAccessExpressionSyntax mae &&
                mae.Expression is IdentifierNameSyntax id &&
                id.Identifier.ValueText == paramName)
            {
                return true;
            }

            if (descendant is ElementAccessExpressionSyntax eae &&
                eae.Expression is IdentifierNameSyntax eid &&
                eid.Identifier.ValueText == paramName)
            {
                return true;
            }


        }

        return false;
    }

    private static bool IsThrowOnlyExpressionBody(MethodDeclarationSyntax methodDeclaration)
    {
        var expr = methodDeclaration.ExpressionBody?.Expression;
        return expr is ThrowExpressionSyntax;
    }

    private static bool IsThrowOnlyBlockBody(MethodDeclarationSyntax methodDeclaration)
    {
        var body = methodDeclaration.Body;
        if (body == null)
        {
            return false;
        }
        if (body.Statements.Count != 1)
        {
            return false;
        }
        return body.Statements[0] is ThrowStatementSyntax;
    }
}
