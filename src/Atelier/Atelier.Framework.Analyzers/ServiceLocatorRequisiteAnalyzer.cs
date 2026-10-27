using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atelier.Framework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ServiceLocatorRequisiteAnalyzer : DiagnosticAnalyzer
{
    private const string CATEGORY = "DependencyInjection";
    private const string REQUISITE_ATTRIBUTE = "Atelier.Framework.Requisitions.RequisiteAttribute";

    private static readonly DiagnosticDescriptor ServiceProviderRequisiteDiagnostic = new DiagnosticDescriptor(
        "ATELIER0606",
        "IServiceProvider is not a requisite",
        "[Requisite] '{0}' is an IServiceProvider; declare each service it needs as its own [Requisite]",
        CATEGORY,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A [Requisite] is a declared edge to a service. An IServiceProvider can resolve anything at runtime, so it declares no edge and hides every dependency it is used for.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(ServiceProviderRequisiteDiagnostic);

    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeMember, SymbolKind.Field, SymbolKind.Property);
    }

    private static void AnalyzeMember(SymbolAnalysisContext context)
    {
        var memberType = context.Symbol switch
        {
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            _ => null,
        };

        if (memberType is null
            || memberType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) != "global::System.IServiceProvider"
            || !context.Symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == REQUISITE_ATTRIBUTE))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            ServiceProviderRequisiteDiagnostic,
            context.Symbol.Locations[0],
            context.Symbol.Name));
    }
}
