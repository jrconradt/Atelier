using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atelier.Framework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ParameterValidationAnalyzer : DiagnosticAnalyzer
{
    public const string DIAGNOSTIC_ID = "ATELIER004";
    private const string CATEGORY = "Validation";

    private static readonly LocalizableString Title =
        "Method parameters must be validated";
    private static readonly LocalizableString MessageFormat =
        "Method '{0}' has parameters that require validation but validation is missing";
    private static readonly LocalizableString Description =
        "All method parameters must be validated to prevent null reference exceptions and ensure data integrity.";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DIAGNOSTIC_ID,
        Title,
        MessageFormat,
        CATEGORY,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description,
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

        if (methodSymbol.Parameters.Length == 0)
        {
            return;
        }

        if (methodSymbol.ContainingType?.TypeKind == TypeKind.Interface)
        {
            return;
        }
        if (methodSymbol.IsAbstract)
        {
            return;
        }
        if (methodDeclaration.Body == null && methodDeclaration.ExpressionBody == null)
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

        if (AnalyzerTestCode.IsTestCode(context))
        {
            return;
        }

        if (!IsFrameworkSurfaceMethod(methodSymbol))
        {
            return;
        }

        var validatableParameters = methodSymbol.Parameters
            .Where(IsValidatableParameter)
            .ToList();

        if (validatableParameters.Count == 0)
        {
            return;
        }

        var hasValidationAttribute = methodSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.Name == "ValidatedAttribute");

        if (hasValidationAttribute)
        {
            return;
        }

        var unvalidated = validatableParameters
            .Where(p => !IsParameterValidated(methodDeclaration, p, context.SemanticModel))
            .ToList();

        if (unvalidated.Count > 0)
        {
            var diagnostic = Diagnostic.Create(
                Rule,
                methodDeclaration.Identifier.GetLocation(),
                methodSymbol.Name);
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static bool IsValidatableParameter(IParameterSymbol parameter)
    {
        if (parameter.Type.IsValueType)
        {
            return false;
        }

        if (parameter.NullableAnnotation == NullableAnnotation.Annotated)
        {
            return false;
        }

        if (parameter.Type.ToDisplayString() == "System.Threading.CancellationToken")
        {
            return false;
        }

        if (parameter.RefKind == RefKind.Out)
        {
            return false;
        }

        return true;
    }

    private static bool IsFrameworkSurfaceMethod(IMethodSymbol methodSymbol)
    {
        if (methodSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.Name == "OperationAttribute"))
        {
            return true;
        }

        var containingType = methodSymbol.ContainingType;
        if (containingType == null)
        {
            return false;
        }

        if (containingType.Name.EndsWith("Service", StringComparison.Ordinal))
        {
            return true;
        }

        return containingType.GetAttributes()
            .Any(a => a.AttributeClass?.Name == "InfrastructureAttribute"
                || a.AttributeClass?.Name == "ServiceDiscoveryAttribute");
    }

    private static bool IsParameterValidated(
        MethodDeclarationSyntax methodDeclaration,
        IParameterSymbol parameter,
        SemanticModel semanticModel)
    {
        SyntaxNode? body = methodDeclaration.Body;
        body ??= methodDeclaration.ExpressionBody?.Expression;
        if (body == null)
        {
            return false;
        }

        foreach (var descendant in body.DescendantNodesAndSelf())
        {
            if (NodeValidatesParameter(descendant, parameter, semanticModel))
            {
                return true;
            }
        }

        return false;
    }

    private static bool NodeValidatesParameter(
        SyntaxNode node,
        IParameterSymbol parameter,
        SemanticModel semanticModel)
    {
        if (node is InvocationExpressionSyntax invocation
            && InvocationGuardsParameter(invocation, parameter, semanticModel))
        {
            return true;
        }

        if (node is BinaryExpressionSyntax binary
            && (binary.IsKind(SyntaxKind.EqualsExpression) || binary.IsKind(SyntaxKind.NotEqualsExpression)))
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
            && PatternIsNullTest(isPattern.Pattern))
        {
            return true;
        }

        if (node is AssignmentExpressionSyntax assignment
            && assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression)
            && BindsToParameter(assignment.Left, parameter, semanticModel)
            && assignment.Right is ThrowExpressionSyntax)
        {
            return true;
        }

        if (node is BinaryExpressionSyntax coalesce
            && coalesce.IsKind(SyntaxKind.CoalesceExpression)
            && BindsToParameter(coalesce.Left, parameter, semanticModel)
            && coalesce.Right is ThrowExpressionSyntax)
        {
            return true;
        }

        return false;
    }

    private static bool InvocationGuardsParameter(
        InvocationExpressionSyntax invocation,
        IParameterSymbol parameter,
        SemanticModel semanticModel)
    {
        var name = (invocation.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.ValueText
            ?? (invocation.Expression as IdentifierNameSyntax)?.Identifier.ValueText;

        if (name is not ("ThrowIfNull" or "ThrowIfNullOrEmpty" or "ThrowIfNullOrWhiteSpace"
            or "IsNullOrEmpty" or "IsNullOrWhiteSpace" or "Validate")
            && name?.StartsWith("Ensure", StringComparison.Ordinal) != true)
        {
            return false;
        }

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (BindsToParameter(argument.Expression, parameter, semanticModel))
            {
                return true;
            }
        }

        return false;
    }

    private static bool PatternIsNullTest(PatternSyntax pattern)
    {
        if (pattern is ConstantPatternSyntax constant
            && constant.Expression.IsKind(SyntaxKind.NullLiteralExpression))
        {
            return true;
        }

        if (pattern is UnaryPatternSyntax unary
            && unary.IsKind(SyntaxKind.NotPattern)
            && unary.Pattern is ConstantPatternSyntax innerConstant
            && innerConstant.Expression.IsKind(SyntaxKind.NullLiteralExpression))
        {
            return true;
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
