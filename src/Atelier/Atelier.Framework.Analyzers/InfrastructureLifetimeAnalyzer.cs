using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atelier.Framework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InfrastructureLifetimeAnalyzer : DiagnosticAnalyzer
{
    private const string CATEGORY = "Infrastructure";

    private static readonly DiagnosticDescriptor SingletonWithStateDiagnostic = new DiagnosticDescriptor(
        "ATELIER0400",
        "Singleton Service Has Mutable State",
        "Service '{0}' is Singleton but has mutable instance fields. Consider Scoped lifetime or make fields readonly.",
        CATEGORY,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Singleton services with mutable state can cause concurrency issues.");

    private static readonly DiagnosticDescriptor ScopedWithoutStateDiagnostic = new DiagnosticDescriptor(
        "ATELIER0401",
        "Scoped Service Without State",
        "Service '{0}' is Scoped but appears stateless. Consider Singleton lifetime for better performance.",
        CATEGORY,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Stateless services can use Singleton lifetime for better performance.");

    private static readonly DiagnosticDescriptor RepositoryNotScopedDiagnostic = new DiagnosticDescriptor(
        "ATELIER0402",
        "Repository Should Be Scoped",
        "Repository '{0}' should use Scoped lifetime to align with transaction boundaries",
        CATEGORY,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Repository classes should be Scoped to align with database transaction boundaries.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            SingletonWithStateDiagnostic,
            ScopedWithoutStateDiagnostic,
            RepositoryNotScopedDiagnostic);

    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeServiceClass, SyntaxKind.ClassDeclaration);
    }

    private void AnalyzeServiceClass(SyntaxNodeAnalysisContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration);

        if (classSymbol == null || !IsServiceClass(classSymbol))
        {
            return;
        }

        var infrastructureAttr = GetInfrastructureAttribute(classSymbol);
        if (infrastructureAttr == null)
        {
            return;
        }

        var lifetime = ResolveLifetimeName(infrastructureAttr) ?? "Singleton";
        var className = classSymbol.Name;

        if (IsRepository(classSymbol) && lifetime != "Scoped")
        {
            var diagnostic = Diagnostic.Create(
                RepositoryNotScopedDiagnostic,
                classDeclaration.Identifier.GetLocation(),
                className);
            context.ReportDiagnostic(diagnostic);
        }

        if (lifetime == "Singleton" && HasMutableInstanceState(classDeclaration))
        {
            var diagnostic = Diagnostic.Create(
                SingletonWithStateDiagnostic,
                classDeclaration.Identifier.GetLocation(),
                className);
            context.ReportDiagnostic(diagnostic);
        }

        if (lifetime == "Scoped" && !HasInstanceState(classDeclaration) && !IsRepository(classSymbol))
        {
            var diagnostic = Diagnostic.Create(
                ScopedWithoutStateDiagnostic,
                classDeclaration.Identifier.GetLocation(),
                className);
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static bool IsServiceClass(INamedTypeSymbol classSymbol)
    {
        return classSymbol.HasAttribute("InfrastructureAttribute");
    }

    private static AttributeData? GetInfrastructureAttribute(INamedTypeSymbol classSymbol)
    {
        return classSymbol.FindAttribute("InfrastructureAttribute");
    }

    private static string? ResolveLifetimeName(AttributeData attribute)
    {
        foreach (var arg in attribute.ConstructorArguments)
        {
            if (arg.Kind == TypedConstantKind.Enum &&
                arg.Type is INamedTypeSymbol enumType &&
                enumType.Name == "InfrastructureLifetime" &&
                arg.Value is not null)
            {
                var name = enumType.GetMembers()
                    .OfType<IFieldSymbol>()
                    .FirstOrDefault(f => f.HasConstantValue && Equals(f.ConstantValue, arg.Value))
                    ?.Name;
                if (!string.IsNullOrEmpty(name))
                {
                    return name;
                }
            }
        }

        return "Singleton";
    }

    private static bool IsRepository(INamedTypeSymbol classSymbol)
    {
        var className = classSymbol.Name;
        return className.EndsWith("Repository", StringComparison.OrdinalIgnoreCase) ||
               classSymbol.AllInterfaces.Any(i => i.Name.EndsWith("Repository", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasMutableInstanceState(ClassDeclarationSyntax classDeclaration)
    {
        var mutableFields = classDeclaration.Members
            .OfType<FieldDeclarationSyntax>()
            .Where(field =>
                !field.Modifiers.Any(SyntaxKind.StaticKeyword) &&
                !field.Modifiers.Any(SyntaxKind.ReadOnlyKeyword) &&
                !field.Modifiers.Any(SyntaxKind.ConstKeyword))
            .ToList();

        var problematicFields = mutableFields
            .Where(field => !HasRequisiteAttribute(field))
            .ToList();

        return problematicFields.Any();
    }

    private static bool HasInstanceState(ClassDeclarationSyntax classDeclaration)
    {
        var instanceFields = classDeclaration.Members
            .OfType<FieldDeclarationSyntax>()
            .Where(field => !field.Modifiers.Any(SyntaxKind.StaticKeyword))
            .Where(field => !HasRequisiteAttribute(field))
            .ToList();

        return instanceFields.Any();
    }

    private static bool HasRequisiteAttribute(FieldDeclarationSyntax field)
    {
        return field.AttributeLists
            .SelectMany(list => list.Attributes)
            .Any(attr => attr.Name.ToString() == "Requisite");
    }
}
