using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atelier.Framework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ClosedSetAuthorizationLiteralAnalyzer : DiagnosticAnalyzer
{
    public const string DIAGNOSTIC_ID = "ATELIER0741";

    private const string CLAIMS_CATALOG = "Atelier.Framework.Identity.Authorization.Claims";
    private const string SCOPES_CATALOG = "Atelier.Framework.Identity.Authorization.Scopes";

    private static readonly DiagnosticDescriptor RawAuthorizationLiteralRule = new DiagnosticDescriptor(
        DIAGNOSTIC_ID,
        "Authorization claim or scope is not a catalog constant",
        "'{0}' supplies a raw authorization value; claims and scopes must reference a const member of {1} so the requirement is a member of the closed catalog. Replace the literal with the matching catalog constant.",
        "Security",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "[RequiresClaim], [RequiresClaimContract], [RequiresScope], [RequiresScopeContract], and [Api] claims accept only const members of the authorization catalog (Atelier.Framework.Identity.Authorization.Claims / Scopes). A raw or typo'd string literal would advertise a requirement outside the closed set and silently never match; it is refused at compile time.",
        customTags: new[] { WellKnownDiagnosticTags.Compiler });

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(RawAuthorizationLiteralRule);

    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeAttribute, SyntaxKind.Attribute);
    }

    private static void AnalyzeAttribute(SyntaxNodeAnalysisContext context)
    {
        var attribute = (AttributeSyntax)context.Node;
        var attributeSymbol = context.SemanticModel.GetSymbolInfo(attribute).Symbol as IMethodSymbol;
        var attributeName = attributeSymbol?.ContainingType?.Name;

        var catalog = ResolveCatalog(attributeName);
        if (catalog == null)
        {
            return;
        }

        var arguments = attribute.ArgumentList?.Arguments;
        if (arguments == null)
        {
            return;
        }

        foreach (var argument in arguments.Value)
        {
            if (argument.NameEquals?.Name.Identifier.ValueText == "Description")
            {
                continue;
            }

            foreach (var expression in FlattenStringExpressions(argument.Expression))
            {
                ValidateExpression(context, expression, catalog);
            }
        }
    }

    private static string? ResolveCatalog(string? attributeName)
    {
        if (attributeName == "RequiresClaimAttribute"
            || attributeName == "RequiresClaimContractAttribute"
            || attributeName == "ApiAttribute")
        {
            return CLAIMS_CATALOG;
        }

        if (attributeName == "RequiresScopeAttribute"
            || attributeName == "RequiresScopeContractAttribute")
        {
            return SCOPES_CATALOG;
        }

        return null;
    }

    private static IEnumerable<ExpressionSyntax> FlattenStringExpressions(ExpressionSyntax root)
    {
        var results = new List<ExpressionSyntax>();
        var stack = new Stack<ExpressionSyntax>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            switch (current)
            {
                case ImplicitArrayCreationExpressionSyntax implicitArray:
                {
                    foreach (var element in implicitArray.Initializer.Expressions)
                    {
                        stack.Push(element);
                    }
                    break;
                }
                case ArrayCreationExpressionSyntax array:
                {
                    if (array.Initializer != null)
                    {
                        foreach (var element in array.Initializer.Expressions)
                        {
                            stack.Push(element);
                        }
                    }
                    break;
                }
                case InitializerExpressionSyntax initializer:
                {
                    foreach (var element in initializer.Expressions)
                    {
                        stack.Push(element);
                    }
                    break;
                }
                case CollectionExpressionSyntax collection:
                {
                    foreach (var element in collection.Elements)
                    {
                        if (element is ExpressionElementSyntax expressionElement)
                        {
                            stack.Push(expressionElement.Expression);
                        }
                    }
                    break;
                }
                default:
                {
                    results.Add(current);
                    break;
                }
            }
        }

        return results;
    }

    private static void ValidateExpression(SyntaxNodeAnalysisContext context,
                                           ExpressionSyntax expression,
                                           string catalog)
    {
        var constant = context.SemanticModel.GetConstantValue(expression);
        if (!constant.HasValue || constant.Value is not string)
        {
            return;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(expression).Symbol;
        if (IsCatalogConstant(symbol, catalog))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            RawAuthorizationLiteralRule,
            expression.GetLocation(),
            expression.ToString(),
            catalog));
    }

    private static bool IsCatalogConstant(ISymbol? symbol,
                                          string catalog)
    {
        if (symbol is not IFieldSymbol field
            || !field.IsConst
            || field.Type.SpecialType != SpecialType.System_String)
        {
            return false;
        }

        var containingType = field.ContainingType;
        while (containingType != null)
        {
            if (containingType.ToDisplayString() == catalog)
            {
                return true;
            }

            containingType = containingType.ContainingType;
        }

        return false;
    }
}
