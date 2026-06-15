using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atelier.Framework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class RedundantConstructorAnalyzer : DiagnosticAnalyzer
{
    public const string DIAGNOSTIC_ID = "ATELIER1600";

    private static readonly DiagnosticDescriptor Rule = new(
        DIAGNOSTIC_ID,
        "Redundant constructor in partial class with [Requisite] fields",
        "Constructor in '{0}' is redundant and should be removed - source generator will create it automatically",
        "CodeGeneration",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Partial classes with [Requisite] fields should not have manual constructors that only pass parameters to base(). The source generator creates these automatically.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeConstructor, SyntaxKind.ConstructorDeclaration);
    }

    private void AnalyzeConstructor(SyntaxNodeAnalysisContext context)
    {
        var constructor = (ConstructorDeclarationSyntax)context.Node;

        var classDeclaration = constructor.Parent as ClassDeclarationSyntax;
        if (classDeclaration == null)
        {
            return;
        }

        if (!classDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            return;
        }

        var semanticModel = context.SemanticModel;
        var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration);
        if (classSymbol == null)
        {
            return;
        }

        var hasRequisiteFields = classSymbol.GetMembers()
            .OfType<IFieldSymbol>()
            .Any(f => f.GetAttributes().Any(a =>
                a.AttributeClass?.Name == "RequisiteAttribute" ||
                a.AttributeClass?.Name == "Requisite"));

        if (!hasRequisiteFields)
        {
            return;
        }

        if (!IsRedundantConstructor(constructor))
        {
            return;
        }

        var diagnostic = Diagnostic.Create(
            Rule,
            constructor.GetLocation(),
            classSymbol.Name);

        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsRedundantConstructor(ConstructorDeclarationSyntax constructor)
    {
        if (constructor.Body == null)
        {
            return false;
        }

        if (constructor.Body.Statements.Count != 0)
        {
            return false;
        }

        if (constructor.Initializer == null)
        {
            return false;
        }

        if (!constructor.Initializer.IsKind(SyntaxKind.BaseConstructorInitializer))
        {
            return false;
        }

        return constructor.Initializer.ArgumentList.Arguments.Count == 0;
    }
}
