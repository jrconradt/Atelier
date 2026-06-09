using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atelier.Framework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AsyncBlockingAnalyzer : DiagnosticAnalyzer
{
    private const string CATEGORY = "Atelier.Performance";

    private static readonly DiagnosticDescriptor ResultPropertyDiagnostic = new DiagnosticDescriptor(
        "ATELIER1300",
        "Synchronous blocking on async operation using .Result",
        "Accessing .Result on '{0}' blocks the thread synchronously. Use 'await' instead to prevent deadlocks and improve performance.",
        CATEGORY,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Using .Result on Task/ValueTask blocks the calling thread and can cause deadlocks, especially in UI or ASP.NET contexts. " +
                     "Use 'await' for proper async flow. Blocking is forbidden uniformly: GetAwaiter().GetResult() is also flagged.");

    private static readonly DiagnosticDescriptor WaitMethodDiagnostic = new DiagnosticDescriptor(
        "ATELIER1301",
        "Synchronous blocking on async operation using .Wait() or GetAwaiter().GetResult()",
        "Calling '{0}' blocks the thread synchronously. Use 'await' instead to prevent deadlocks and improve performance.",
        CATEGORY,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Using .Wait() or GetAwaiter().GetResult() on Task/ValueTask blocks the calling thread and can cause deadlocks, especially in UI or ASP.NET contexts. " +
                     "Use 'await' for proper async flow. Blocking is forbidden uniformly.");

    private static readonly DiagnosticDescriptor TaskWaitDiagnostic = new DiagnosticDescriptor(
        "ATELIER1302",
        "Synchronous blocking on multiple tasks using Task.WaitAll/WaitAny",
        "Using Task.{0} blocks the thread synchronously. Use 'await Task.WhenAll()' or 'await Task.WhenAny()' instead.",
        CATEGORY,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Task.WaitAll and Task.WaitAny block the calling thread. Use Task.WhenAll and Task.WhenAny with await instead.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            ResultPropertyDiagnostic,
            WaitMethodDiagnostic,
            TaskWaitDiagnostic);
    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;

        if (AnalyzerTestCode.IsTestCode(context))
        {
            return;
        }

        if (memberAccess.Name.Identifier.Text == "Result")
        {
            var typeInfo = context.SemanticModel.GetTypeInfo(memberAccess.Expression);
            if (IsTaskType(typeInfo.Type))
            {
                var diagnostic = Diagnostic.Create(
                    ResultPropertyDiagnostic,
                    memberAccess.Name.GetLocation(),
                    typeInfo.Type?.Name ?? "Task");
                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    private void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (AnalyzerTestCode.IsTestCode(context))
        {
            return;
        }

        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var methodName = memberAccess.Name.Identifier.Text;

            if (methodName == "Wait")
            {
                var typeInfo = context.SemanticModel.GetTypeInfo(memberAccess.Expression);
                if (IsTaskType(typeInfo.Type))
                {
                    var diagnostic = Diagnostic.Create(
                        WaitMethodDiagnostic,
                        memberAccess.Name.GetLocation(),
                        $".Wait() on {typeInfo.Type?.Name ?? "Task"}");
                    context.ReportDiagnostic(diagnostic);
                }
            }
            else if (methodName == "GetResult"
                && IsAwaiterFromTask(memberAccess.Expression, context))
            {
                var diagnostic = Diagnostic.Create(
                    WaitMethodDiagnostic,
                    memberAccess.Name.GetLocation(),
                    "GetAwaiter().GetResult()");
                context.ReportDiagnostic(diagnostic);
            }
            else if (methodName == "WaitAll" || methodName == "WaitAny")
            {

                if (memberAccess.Expression is IdentifierNameSyntax identifierName &&
                    identifierName.Identifier.Text == "Task")
                {
                    var symbolInfo = context.SemanticModel.GetSymbolInfo(identifierName);
                    if (symbolInfo.Symbol is INamedTypeSymbol typeSymbol &&
                        typeSymbol.Name == "Task" &&
                        typeSymbol.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks")
                    {
                        var diagnostic = Diagnostic.Create(
                            TaskWaitDiagnostic,
                            memberAccess.Name.GetLocation(),
                            methodName);
                        context.ReportDiagnostic(diagnostic);
                    }
                }
            }
        }
    }

    private static bool IsAwaiterFromTask(ExpressionSyntax receiver, SyntaxNodeAnalysisContext context)
    {
        if (receiver is InvocationExpressionSyntax awaiterInvocation
            && awaiterInvocation.Expression is MemberAccessExpressionSyntax awaiterMember
            && awaiterMember.Name.Identifier.Text == "GetAwaiter")
        {
            var taskTypeInfo = context.SemanticModel.GetTypeInfo(awaiterMember.Expression);
            if (IsTaskType(taskTypeInfo.Type))
            {
                return true;
            }
        }

        var awaiterType = context.SemanticModel.GetTypeInfo(receiver).Type;
        return IsTaskAwaiterType(awaiterType);
    }

    private static bool IsTaskAwaiterType(ITypeSymbol? type)
    {
        if (type == null)
        {
            return false;
        }

        var typeName = type.Name;
        var namespaceName = type.ContainingNamespace?.ToDisplayString();

        return (typeName == "TaskAwaiter"
                || typeName == "ValueTaskAwaiter"
                || typeName == "ConfiguredTaskAwaitable"
                || typeName == "ConfiguredValueTaskAwaitable")
            && namespaceName != null
            && namespaceName.StartsWith("System.", StringComparison.Ordinal);
    }

    private static bool IsTaskType(ITypeSymbol? type)
    {
        if (type == null)
        {
            return false;
        }

        var typeName = type.Name;
        var namespaceName = type.ContainingNamespace?.ToDisplayString();

        return (typeName == "Task" || typeName == "ValueTask") &&
               namespaceName == "System.Threading.Tasks";
    }
}
