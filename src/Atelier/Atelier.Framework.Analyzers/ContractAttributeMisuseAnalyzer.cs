using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atelier.Framework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractAttributeMisuseAnalyzer : DiagnosticAnalyzer
{
    private const string CATEGORY = "Atelier.Patterns";

    private static readonly DiagnosticDescriptor ContractOnInterfaceDiagnostic = new DiagnosticDescriptor(
        "ATELIER1500",
        "[Contract] attribute on interface - should only be on DTOs",
        "Interface '{0}' has [Contract] attribute. [Contract] is exclusively for DTOs (data classes), not interfaces. Remove the attribute.",
        CATEGORY,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "[Contract] attribute is exclusively for pure data transfer objects (DTOs). " +
                     "Interfaces define behavior contracts, not data contracts. For service interfaces, use [Facility] if exposed remotely.");

    private static readonly DiagnosticDescriptor ContractOnAbstractClassDiagnostic = new DiagnosticDescriptor(
        "ATELIER1501",
        "[Contract] attribute on abstract class - should only be on DTOs",
        "Abstract class '{0}' has [Contract] attribute. [Contract] is exclusively for concrete DTOs. Abstract classes define inheritance hierarchies, not data contracts.",
        CATEGORY,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "[Contract] attribute is exclusively for concrete data transfer objects (DTOs). " +
                     "Abstract classes are used for polymorphism and shared behavior, not for data contracts.");

    private static readonly DiagnosticDescriptor ContractOnServiceDiagnostic = new DiagnosticDescriptor(
        "ATELIER1502",
        "[Contract] attribute on service class - should only be on DTOs",
        "Service class '{0}' has [Contract] attribute. [Contract] is exclusively for DTOs, not service classes. Remove the attribute.",
        CATEGORY,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "[Contract] attribute is exclusively for pure data transfer objects (DTOs). " +
                     "Service classes should use [Infrastructure] attribute instead.");

    private static readonly DiagnosticDescriptor ContractOnBehaviorClassDiagnostic = new DiagnosticDescriptor(
        "ATELIER1503",
        "[Contract] attribute on behavior class - should only be on DTOs",
        "Class '{0}' has [Contract] attribute but contains behavior (methods/dependencies). [Contract] is exclusively for pure DTOs (data-only classes).",
        CATEGORY,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "[Contract] attribute is exclusively for pure data transfer objects (DTOs) - classes with only properties/fields. " +
                     "Classes with methods, service dependencies ([Requisite]), or state management should not use [Contract].");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            ContractOnInterfaceDiagnostic,
            ContractOnAbstractClassDiagnostic,
            ContractOnServiceDiagnostic,
            ContractOnBehaviorClassDiagnostic);
    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeTypeDeclaration, SyntaxKind.ClassDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeTypeDeclaration, SyntaxKind.InterfaceDeclaration);
    }

    private void AnalyzeTypeDeclaration(SyntaxNodeAnalysisContext context)
    {
        var typeDeclaration = context.Node;
        INamedTypeSymbol? typeSymbol = null;
        string typeName;

        if (typeDeclaration is ClassDeclarationSyntax classDeclaration)
        {
            typeSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration);
            typeName = classDeclaration.Identifier.Text;
        }
        else if (typeDeclaration is InterfaceDeclarationSyntax interfaceDeclaration)
        {
            typeSymbol = context.SemanticModel.GetDeclaredSymbol(interfaceDeclaration);
            typeName = interfaceDeclaration.Identifier.Text;
        }
        else
        {
            return;
        }

        if (typeSymbol == null)
        {
            return;
        }

        var contractAttribute = typeSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "ContractAttribute");

        if (contractAttribute == null)
        {
            return;
        }

        var attributeLocation = GetAttributeLocation(typeDeclaration, "Contract");

        if (typeSymbol.TypeKind == TypeKind.Interface)
        {
            var diagnostic = Diagnostic.Create(
                ContractOnInterfaceDiagnostic,
                attributeLocation,
                typeName);
            context.ReportDiagnostic(diagnostic);
            return;
        }

        if (typeSymbol.IsAbstract)
        {
            var diagnostic = Diagnostic.Create(
                ContractOnAbstractClassDiagnostic,
                attributeLocation,
                typeName);
            context.ReportDiagnostic(diagnostic);
            return;
        }

        if (IsServiceLikeClass(typeSymbol))
        {
            var diagnostic = Diagnostic.Create(
                ContractOnServiceDiagnostic,
                attributeLocation,
                typeName);
            context.ReportDiagnostic(diagnostic);
            return;
        }

        if (HasBehavior(typeSymbol))
        {
            var diagnostic = Diagnostic.Create(
                ContractOnBehaviorClassDiagnostic,
                attributeLocation,
                typeName);
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static Location GetAttributeLocation(SyntaxNode typeDeclaration, string attributeName)
    {
        AttributeListSyntax? attributeList = null;

        if (typeDeclaration is ClassDeclarationSyntax classDeclaration)
        {
            attributeList = classDeclaration.AttributeLists
                .SelectMany(al => al.Attributes)
                .FirstOrDefault(a => a.Name.ToString().Contains(attributeName, StringComparison.OrdinalIgnoreCase))
                ?.Parent as AttributeListSyntax;
        }
        else if (typeDeclaration is InterfaceDeclarationSyntax interfaceDeclaration)
        {
            attributeList = interfaceDeclaration.AttributeLists
                .SelectMany(al => al.Attributes)
                .FirstOrDefault(a => a.Name.ToString().Contains(attributeName, StringComparison.OrdinalIgnoreCase))
                ?.Parent as AttributeListSyntax;
        }

        return attributeList?.GetLocation() ?? typeDeclaration.GetLocation();
    }

    private static bool IsServiceLikeClass(INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.HasAttribute("InfrastructureAttribute"))
        {
            return true;
        }

        var baseType = typeSymbol.BaseType;
        while (baseType != null)
        {
            if (baseType.Name.Contains("OfferingBase", StringComparison.OrdinalIgnoreCase) ||
                baseType.Name.Contains("ProductBase", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            baseType = baseType.BaseType;
        }
        return false;
    }

    private static bool HasBehavior(INamedTypeSymbol typeSymbol)
    {

        var hasMethods = typeSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Any(m => m.MethodKind != MethodKind.Constructor &&
                      m.MethodKind != MethodKind.PropertyGet &&
                      m.MethodKind != MethodKind.PropertySet &&
                      m.MethodKind != MethodKind.StaticConstructor &&
                      !m.IsImplicitlyDeclared);

        if (hasMethods)
        {
            return true;
        }

        var hasRequisites = typeSymbol.GetMembers()
            .OfType<IFieldSymbol>()
            .Any(f => f.HasAttribute("RequisiteAttribute"));

        if (hasRequisites)
        {
            return true;
        }

        var hasInfrastructure = typeSymbol.HasAttribute("InfrastructureAttribute");

        if (hasInfrastructure)
        {
            return true;
        }


        var hasComplexProperties = typeSymbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Any(p => HasAccessorBody(p.GetMethod) || HasAccessorBody(p.SetMethod));

        return hasComplexProperties;
    }

    private static bool HasAccessorBody(IMethodSymbol? accessor)
    {
        if (accessor is null)
        {
            return false;
        }
        foreach (var syntaxRef in accessor.DeclaringSyntaxReferences)
        {
            var node = syntaxRef.GetSyntax();
            if (node is AccessorDeclarationSyntax accDecl)
            {
                if (accDecl.Body is not null || accDecl.ExpressionBody is not null)
                {
                    return true;
                }
            }
            else if (node is ArrowExpressionClauseSyntax)
            {
                return true;
            }
        }
        return false;
    }
}
