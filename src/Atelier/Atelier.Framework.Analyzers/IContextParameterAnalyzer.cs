using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atelier.Framework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IContextParameterAnalyzer : DiagnosticAnalyzer
{
    private const string CATEGORY = "Atelier.Patterns";

    private static readonly DiagnosticDescriptor IContextParameterDiagnostic = new DiagnosticDescriptor(
        "ATELIER1100",
        "IContext parameter in local service - use ambient context instead",
        "Method '{0}' accepts IContext as parameter. Local services should use the ambient 'this.Context' property instead.",
        CATEGORY,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "IContext should NEVER be passed as a method parameter in local services. " +
                     "Use the ambient 'this.Context' property. " +
                     "Only [Facility] interfaces for remote services should accept IContext parameters.");

    private static readonly DiagnosticDescriptor IContextInNonFacilityDiagnostic = new DiagnosticDescriptor(
        "ATELIER1101",
        "IContext parameter in non-Facility interface",
        "Interface '{0}' method '{1}' accepts IContext but interface is not marked with [Facility]. " +
        "Local service interfaces should not have IContext parameters.",
        CATEGORY,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Only interfaces marked with [Facility] attribute should have IContext parameters. " +
                     "Local service interfaces should omit IContext and let implementations use ambient context.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            IContextParameterDiagnostic,
            IContextInNonFacilityDiagnostic);
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

        if (methodSymbol == null)
        {
            return;
        }

        var icontextParameter = methodSymbol.Parameters
            .FirstOrDefault(p => IsIContextType(p.Type));

        if (icontextParameter == null)
        {
            return;
        }

        var containingType = methodSymbol.ContainingType;

        if (containingType.TypeKind == TypeKind.Interface)
        {

            var hasFacilityAttribute = containingType.GetAttributes()
                .Any(a => a.AttributeClass?.Name == "FacilityAttribute");

            if (!hasFacilityAttribute)
            {

                var parameterSyntax = methodDeclaration.ParameterList.Parameters
                    .FirstOrDefault(p => p.Identifier.Text == icontextParameter.Name);

                if (parameterSyntax != null)
                {
                    var diagnostic = Diagnostic.Create(
                        IContextInNonFacilityDiagnostic,
                        parameterSyntax.GetLocation(),
                        containingType.Name,
                        methodSymbol.Name);
                    context.ReportDiagnostic(diagnostic);
                }
            }
        }
        else if (containingType.TypeKind == TypeKind.Class)
        {

            var isServiceClass = containingType.GetAttributes()
                .Any(a => a.AttributeClass?.Name == "InfrastructureAttribute");
            if (isServiceClass)
            {

                var implementsFacility = containingType.AllInterfaces
                    .Any(i => i.GetAttributes()
                        .Any(a => a.AttributeClass?.Name == "FacilityAttribute"));

                if (!implementsFacility)
                {

                    var parameterSyntax = methodDeclaration.ParameterList.Parameters
                        .FirstOrDefault(p => p.Identifier.Text == icontextParameter.Name);

                    if (parameterSyntax != null)
                    {
                        var diagnostic = Diagnostic.Create(
                            IContextParameterDiagnostic,
                            parameterSyntax.GetLocation(),
                            methodSymbol.Name);
                        context.ReportDiagnostic(diagnostic);
                    }
                }
            }
        }
    }

    private static bool IsIContextType(ITypeSymbol type)
    {
        return type.Name == "IContext" &&
               type.ContainingNamespace?.ToDisplayString()
                   .StartsWith("Atelier.Framework.Context", StringComparison.Ordinal) == true;
    }
}
