using System.Collections.Immutable;
using Atelier.Framework.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ContractEnforcementAnalyzer : DiagnosticAnalyzer
{
    public const string DIAGNOSTIC_ID = "ATELIER0200";
    private const string CATEGORY = "Contract";

    private static readonly LocalizableString Title =
        "DTOs must define contracts";
    private static readonly LocalizableString MessageFormat =
        "Class '{0}' appears to be a DTO but is missing a [Contract] attribute";
    private static readonly LocalizableString Description =
        "All transfer objects must be marked with [Contract] attribute for versioning, validation, and serialization.";

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
        context.RegisterSyntaxNodeAction(AnalyzeClassDeclaration, SyntaxKind.ClassDeclaration);
    }

    private void AnalyzeClassDeclaration(SyntaxNodeAnalysisContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration);

        if (classSymbol == null
            || classSymbol.IsAbstract
            || classSymbol.IsStatic)
        {
            return;
        }

        if (!IsDTOClass(classSymbol))
        {
            return;
        }

        if (IsExcludedFromContractRequirement(classSymbol))
        {
            return;
        }

        var hasContractAttribute = classSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.Name == "ContractAttribute");

        if (!hasContractAttribute)
        {
            var diagnostic = Diagnostic.Create(
                Rule,
                classDeclaration.Identifier.GetLocation(),
                classSymbol.Name);
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static bool IsDTOClass(INamedTypeSymbol classSymbol)
    {
        if (DerivesFromAttribute(classSymbol))
        {
            return false;
        }

        var publicProperties = classSymbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => p.DeclaredAccessibility == Accessibility.Public && !p.IsStatic && !p.IsIndexer)
            .ToList();

        if (publicProperties.Count < 2)
        {
            return false;
        }

        var allDataProperties = publicProperties.All(IsDataProperty);
        if (!allDataProperties)
        {
            return false;
        }

        if (classSymbol.IsRecord)
        {
            return true;
        }

        var hasOrdinaryPublicMethods = classSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Any(m => m.DeclaredAccessibility == Accessibility.Public
                && m.MethodKind == MethodKind.Ordinary
                && !m.IsImplicitlyDeclared);

        return !hasOrdinaryPublicMethods;
    }

    private static bool DerivesFromAttribute(INamedTypeSymbol classSymbol)
    {
        var baseType = classSymbol.BaseType;
        while (baseType != null)
        {
            if (baseType.Name == "Attribute"
                && baseType.ContainingNamespace?.ToDisplayString() == "System")
            {
                return true;
            }

            baseType = baseType.BaseType;
        }

        return false;
    }

    private static bool IsDataProperty(IPropertySymbol property)
    {
        if (property.GetMethod == null)
        {
            return false;
        }

        if (property.SetMethod == null)
        {
            return true;
        }

        return property.SetMethod.IsInitOnly;
    }

    private static bool IsExcludedFromContractRequirement(INamedTypeSymbol classSymbol)
    {
        var namespaceName = classSymbol.ContainingNamespace.ToDisplayString();

        if (namespaceName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase) ||
            namespaceName.Contains(".Tests.", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (classSymbol.GetAttributes().Any(a =>
            a.AttributeClass?.Name == "InfrastructureAttribute" ||
            a.AttributeClass?.Name == "ServiceDiscoveryAttribute" ||
            a.AttributeClass?.Name == "AppContainerAttribute"))
        {
            return true;
        }



        var baseType = classSymbol.BaseType;
        while (baseType != null && baseType.Name != "Object")
        {
            var n = baseType.Name;
            if (n.Contains("Service", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("Controller", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("Handler", StringComparison.OrdinalIgnoreCase) ||
                n.Equals("Command", StringComparison.Ordinal) ||
                n.Contains("Middleware", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("Factory", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("Registry", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("Interceptor", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("Provider", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("Strategy", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("Pipeline", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("Builder", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("Manager", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("Validator", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("Generator", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("Loader", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("Repository", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            baseType = baseType.BaseType;
        }

        var hasNonDataMethods = classSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Any(m => m.MethodKind == MethodKind.Ordinary && !m.IsImplicitlyDeclared);
        if (hasNonDataMethods)
        {
            return true;
        }

        var interfaces = classSymbol.AllInterfaces;
        if (interfaces.Any(i =>
            i.Name.StartsWith("IService", StringComparison.OrdinalIgnoreCase) ||

            i.Name.StartsWith("IOperation", StringComparison.OrdinalIgnoreCase) ||

            i.Name.StartsWith("IHandler", StringComparison.OrdinalIgnoreCase) ||

            i.Name.EndsWith("Handler", StringComparison.OrdinalIgnoreCase) ||

            i.Name == "IDisposable" ||

            i.Name == "IAsyncDisposable"))
        {
            return true;
        }

        if (classSymbol.TypeKind == TypeKind.Delegate)
        {
            return true;
        }

        return false;
    }
}
