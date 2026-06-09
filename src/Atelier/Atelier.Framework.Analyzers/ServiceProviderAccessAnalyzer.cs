using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atelier.Framework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ServiceProviderAccessAnalyzer : DiagnosticAnalyzer
{
    public const string DIAGNOSTIC_ID = "ATELIER001";

    private static readonly DiagnosticDescriptor ServiceProviderAccessRule = new(
        DIAGNOSTIC_ID,
        "ServiceProvider access detected",
        "Service '{0}' accesses ServiceProvider directly. Use explicit [Requisite] dependencies instead.",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Direct ServiceProvider access creates hidden dependencies and reduces testability. Use explicit [Requisite] dependencies instead.");

    private static readonly DiagnosticDescriptor ServiceProviderPropertyRule = new(
        "ATELIER002",
        "ServiceProvider property access detected",
        "Service '{0}' accesses ServiceProvider property. Use explicit [Requisite] dependencies instead.",
        "Architecture",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Direct ServiceProvider property access creates hidden dependencies. Use explicit [Requisite] dependencies instead.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(ServiceProviderAccessRule, ServiceProviderPropertyRule);

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

        if (memberAccess.Name.Identifier.Text == "ServiceProvider")
        {
            var containingSymbol = context.ContainingSymbol;
            if (containingSymbol != null && IsServiceClass(containingSymbol))
            {
                var diagnostic = Diagnostic.Create(
                    ServiceProviderPropertyRule,
                    memberAccess.GetLocation(),
                    containingSymbol.Name);
                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    private void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return;
        }

        if (memberAccess.Name.Identifier.Text != "GetService"
            && memberAccess.Name.Identifier.Text != "GetRequiredService")
        {
            return;
        }

        if (!IsServiceProviderTyped(context.SemanticModel,
                                    memberAccess.Expression,
                                    context.CancellationToken))
        {
            return;
        }

        var containingSymbol = context.ContainingSymbol;
        if (containingSymbol != null
            && IsServiceClass(containingSymbol)
            && !IsAllowlistedCompositionRoot(containingSymbol))
        {
            var diagnostic = Diagnostic.Create(
                ServiceProviderAccessRule,
                invocation.GetLocation(),
                containingSymbol.Name);
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static bool IsServiceProviderTyped(SemanticModel semanticModel,
                                               ExpressionSyntax expression,
                                               CancellationToken cancellationToken)
    {
        var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
        var type = typeInfo.Type ?? typeInfo.ConvertedType;
        if (type == null)
        {
            return false;
        }

        if (IsServiceProviderType(type))
        {
            return true;
        }

        foreach (var implemented in type.AllInterfaces)
        {
            if (IsServiceProviderType(implemented))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsServiceProviderType(ITypeSymbol type)
    {
        return type.Name == "IServiceProvider"
            && type.ContainingNamespace?.ToDisplayString() == "System";
    }

    private static bool IsServiceClass(ISymbol symbol)
    {
        return symbol.ContainingType.HasAttribute("InfrastructureAttribute");
    }

    private static bool IsAllowlistedCompositionRoot(ISymbol symbol)
    {
        return symbol.ContainingType?.Name == "ServiceProviderOfferingProvider";
    }
}
