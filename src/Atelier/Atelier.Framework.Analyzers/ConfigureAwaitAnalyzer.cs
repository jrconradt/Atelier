using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Atelier.Framework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ConfigureAwaitAnalyzer : DiagnosticAnalyzer
{
    private const string CATEGORY = "Atelier.Performance";

    private static readonly DiagnosticDescriptor MissingConfigureAwaitDiagnostic = new DiagnosticDescriptor(
        "ATELIER1200",
        "Missing ConfigureAwait(false) in library code",
        "Await expression in library code should use ConfigureAwait(false) to avoid capturing synchronization context",
        CATEGORY,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Library code should use ConfigureAwait(false) on all awaits to avoid unnecessary context captures, " +
                     "prevent deadlocks in synchronous consumers, and improve performance. " +
                     "Only application code (UI, ASP.NET controllers) should use default context capture.");

    private static readonly DiagnosticDescriptor ConfigureAwaitTrueDiagnostic = new DiagnosticDescriptor(
        "ATELIER1201",
        "ConfigureAwait(true) in library code - should be false",
        "Library code uses ConfigureAwait(true). Change to ConfigureAwait(false) for better performance.",
        CATEGORY,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "ConfigureAwait(true) captures synchronization context unnecessarily in library code. Use false instead.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            MissingConfigureAwaitDiagnostic,
            ConfigureAwaitTrueDiagnostic);
    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeAwaitExpression, SyntaxKind.AwaitExpression);
    }

    private void AnalyzeAwaitExpression(SyntaxNodeAnalysisContext context)
    {
        var awaitExpression = (AwaitExpressionSyntax)context.Node;

        if (AnalyzerTestCode.IsTestCode(context))
        {
            return;
        }

        if (IsApplicationCode(context))
        {
            return;
        }

        if (awaitExpression.Expression is InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                if (memberAccess.Name.Identifier.Text == "ConfigureAwait")
                {

                    if (invocation.ArgumentList.Arguments.Count > 0)
                    {
                        var argument = invocation.ArgumentList.Arguments[0];
                        var constantValue = context.SemanticModel.GetConstantValue(argument.Expression);

                        if (constantValue.HasValue && constantValue.Value is bool boolValue)
                        {
                            if (boolValue == true)
                            {

                                var diagnostic = Diagnostic.Create(
                                    ConfigureAwaitTrueDiagnostic,
                                    argument.GetLocation());
                                context.ReportDiagnostic(diagnostic);
                            }

                            return;
                        }
                    }
                    return;
                }
            }
        }

        var awaitedType = context.SemanticModel.GetTypeInfo(awaitExpression.Expression).Type;

        if (IsAwaitableType(awaitedType))
        {
            var diagnostic = Diagnostic.Create(
                MissingConfigureAwaitDiagnostic,
                awaitExpression.GetLocation());
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static bool IsApplicationCode(SyntaxNodeAnalysisContext context)
    {
        var options = context.Options.AnalyzerConfigOptionsProvider
            .GetOptions(context.Node.SyntaxTree);

        if (options.TryGetValue("atelier_async_code_kind", out var kind))
        {
            return string.Equals(kind, "application", StringComparison.OrdinalIgnoreCase);
        }

        if (options.TryGetValue("build_property.AtelierAsyncCodeKind", out var buildKind))
        {
            return string.Equals(buildKind, "application", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsAwaitableType(ITypeSymbol? type)
    {
        if (type == null)
        {
            return false;
        }

        var typeName = type.Name;
        return typeName == "Task" ||
               typeName == "ValueTask" ||
               typeName == "ConfiguredTaskAwaitable" ||
               typeName == "ConfiguredValueTaskAwaitable";
    }
}
