using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atelier.Framework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class NoManualInstantiationAnalyzer : DiagnosticAnalyzer
{
    public const string DIAGNOSTIC_ID_LIFECYCLE = "ATE1001";
    public const string DIAGNOSTIC_ID_CONTRACT = "ATE1002";
    public const string DIAGNOSTIC_ID_POOLED_RETURN = "ATE1003";
    public const string DIAGNOSTIC_ID_SERVICE = "ATE1004";

    private static readonly LocalizableString TitleLifecycle = "No manual instantiation of lifecycle-managed types";
    private static readonly LocalizableString MessageFormatLifecycle = "Type '{0}' is a lifecycle attribute and should be instantiated via IFactory<{0}> or DI";
    private static readonly LocalizableString DescriptionLifecycle = "Types with lifecycle attributes should not be manually instantiated using 'new'";

    private static readonly LocalizableString TitleContract = "No manual instantiation of contract types";
    private static readonly LocalizableString MessageFormatContract = "Type '{0}' is a contract and should be instantiated via IFactory<{0}>";
    private static readonly LocalizableString DescriptionContract = "Contract types should be instantiated through factories for proper validation";

    private static readonly LocalizableString TitlePooledReturn = "Pooled instance not returned";
    private static readonly LocalizableString MessageFormatPooledReturn = "Pooled type '{0}' instance should be returned to the pool using factory.Return() or serviceProvider.Return()";
    private static readonly LocalizableString DescriptionPooledReturn = "Pooled instances must be returned to the pool to prevent memory leaks";

    private static readonly DiagnosticDescriptor RuleLifecycle = new DiagnosticDescriptor(
        DIAGNOSTIC_ID_LIFECYCLE,
        TitleLifecycle,
        MessageFormatLifecycle,
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor RuleContract = new DiagnosticDescriptor(
        DIAGNOSTIC_ID_CONTRACT,
        TitleContract,
        MessageFormatContract,
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor RulePooledReturn = new DiagnosticDescriptor(
        DIAGNOSTIC_ID_POOLED_RETURN,
        TitlePooledReturn,
        MessageFormatPooledReturn,
        "Usage",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor RuleService = new DiagnosticDescriptor(
        DIAGNOSTIC_ID_SERVICE,
        "No manual instantiation of service types",
        "Service type '{0}' must be obtained via an explicit [Requisite] dependency, not constructed with 'new'. Constructing it bypasses needs-based authorization.",
        "Usage",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Service types ([Infrastructure]/[ServiceDiscovery]) may only be reached through declared [Requisite] dependencies so the dependency graph is the sole compile-time authorization for service-to-service access. Manually constructing one bypasses that boundary.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(RuleLifecycle, RuleContract, RulePooledReturn, RuleService);

    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
    }

    private void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
    {
        var objectCreation = (ObjectCreationExpressionSyntax)context.Node;
        var typeSymbol = context.SemanticModel.GetTypeInfo(objectCreation).Type as INamedTypeSymbol;

        if (typeSymbol == null)
        {
            return;
        }

        var typeName = typeSymbol.ToDisplayString();

        if (HasLifecycleAttribute(typeSymbol))
        {
            var diagnostic = Diagnostic.Create(
                RuleLifecycle,
                objectCreation.GetLocation(),
                typeName);
            context.ReportDiagnostic(diagnostic);
        }

        if (HasContractAttribute(typeSymbol))
        {
            var diagnostic = Diagnostic.Create(
                RuleContract,
                objectCreation.GetLocation(),
                typeName);
            context.ReportDiagnostic(diagnostic);
        }

        if (IsPooledType(typeSymbol) && !IsInReturnStatement(objectCreation))
        {
            var diagnostic = Diagnostic.Create(
                RulePooledReturn,
                objectCreation.GetLocation(),
                typeName);
            context.ReportDiagnostic(diagnostic);
        }

        if (IsServiceType(typeSymbol)
            && !AnalyzerTestCode.IsTestCode(context))
        {
            var diagnostic = Diagnostic.Create(
                RuleService,
                objectCreation.GetLocation(),
                typeName);
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static bool IsServiceType(INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.IsAbstract)
        {
            return false;
        }

        return typeSymbol.HasAttribute("InfrastructureAttribute")
            || typeSymbol.HasAttribute("ServiceDiscoveryAttribute");
    }

    private static bool HasLifecycleAttribute(INamedTypeSymbol typeSymbol)
    {
        return typeSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.Name.Contains("Lifecycle") == true ||
                     a.AttributeClass?.Name == "SingletonAttribute" ||
                     a.AttributeClass?.Name == "ScopedAttribute" ||
                     a.AttributeClass?.Name == "TransientAttribute");
    }

    private static bool HasContractAttribute(INamedTypeSymbol typeSymbol)
    {
        return typeSymbol.HasAttribute("ContractAttribute");
    }

    private static bool IsPooledType(INamedTypeSymbol typeSymbol)
    {
        return typeSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.Name.Contains("Pooled") == true ||
                     a.AttributeClass?.Name == "ObjectPoolAttribute");
    }

    private static bool IsInReturnStatement(ObjectCreationExpressionSyntax objectCreation)
    {
        var parent = objectCreation.Parent;
        while (parent != null)
        {
            if (parent is ReturnStatementSyntax)
            {
                return true;
            }

            parent = parent.Parent;
        }
        return false;
    }
}
