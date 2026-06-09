using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atelier.Framework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class AtelierPublicConstructorAnalyzer : DiagnosticAnalyzer
{
    public const string DIAGNOSTIC_ID = "ATELIER1610";

    private static readonly DiagnosticDescriptor Rule = new(
        DIAGNOSTIC_ID,
        "IAtelier-implementing class must not declare a public constructor",
        "IAtelier-implementing class '{0}' must not declare a public constructor. " +
            "Use [Requisite] fields for dependencies and a Configure(...) fluent method " +
            "for per-instance state. See ElasticsearchLoggingStrategy or GenericOidcProvider " +
            "for the canonical pattern.",
        "Atelier.Patterns",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "IAtelier-implementing partial classes must rely on the requisites " +
            "source generator to emit their public constructor. Dependencies are declared " +
            "via [Requisite] fields; per-instance state is set through a Configure(...) " +
            "fluent method. A `protected ClassName()` parameterless constructor is the " +
            "documented escape hatch for derived-class chaining.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
        {
            throw new System.ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeClassDeclaration, SyntaxKind.ClassDeclaration);
    }

    private void AnalyzeClassDeclaration(SyntaxNodeAnalysisContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;

        if (!classDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            return;
        }

        var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration);
        if (classSymbol == null)
        {
            return;
        }

        if (!ImplementsIAtelier(classSymbol))
        {
            return;
        }



        foreach (var ctor in classDeclaration.Members.OfType<ConstructorDeclarationSyntax>())
        {
            if (!ctor.Modifiers.Any(SyntaxKind.PublicKeyword))
            {
                continue;
            }

            if (ctor.ParameterList.Parameters.Count == 0)
            {

                continue;
            }

            var ctorSymbol = context.SemanticModel.GetDeclaredSymbol(ctor);
            if (ctorSymbol != null && ctorSymbol.IsImplicitlyDeclared)
            {
                continue;
            }

            var diagnostic = Diagnostic.Create(
                Rule,
                ctor.Identifier.GetLocation(),
                classSymbol.Name);
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static bool ImplementsIAtelier(INamedTypeSymbol typeSymbol)
    {
        var current = typeSymbol;
        while (current != null)
        {
            if (current.AllInterfaces.Any(i => i.Name == "IAtelier"))
            {
                return true;
            }
            current = current.BaseType;
        }
        return false;
    }
}
