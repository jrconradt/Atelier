using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atelier.Framework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InfrastructureAttributeRequiredAnalyzer : DiagnosticAnalyzer
{
    public const string DIAGNOSTIC_ID = "ATELIER1402";

    private static readonly DiagnosticDescriptor MissingInfrastructureAttribute = new DiagnosticDescriptor(
        DIAGNOSTIC_ID,
        "Requisite dependency target missing [Infrastructure] attribute",
        "Class '{0}' is used as a [Requisite] dependency but is not marked with [Infrastructure]. Add [Infrastructure(InfrastructureLifetime.Scoped)] so it can be auto-discovered and registered.",
        "DependencyInjection",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Concrete classes injected through a [Requisite] field must be registered with [Infrastructure] for auto-discovery; otherwise resolution fails at runtime.",
        customTags: new[] { "CompilationEnd" });

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(MissingInfrastructureAttribute);

    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
        {
            throw new System.ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            var requisiteTargets = new ConcurrentBag<INamedTypeSymbol>();

            start.RegisterSymbolAction(symbolContext =>
            {
                var type = (INamedTypeSymbol)symbolContext.Symbol;

                foreach (var field in type.GetMembers().OfType<IFieldSymbol>())
                {
                    if (!HasRequisiteAttribute(field))
                    {
                        continue;
                    }

                    if (field.Type is INamedTypeSymbol targetType)
                    {
                        requisiteTargets.Add(targetType);
                    }
                }
            }, SymbolKind.NamedType);

            start.RegisterCompilationEndAction(end =>
            {
                var reported = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

                foreach (var target in requisiteTargets)
                {
                    if (!IsConcreteRegisterableClass(target)
                        || HasInfrastructureAttribute(target)
                        || !reported.Add(target))
                    {
                        continue;
                    }

                    if (!SymbolEqualityComparer.Default.Equals(target.ContainingAssembly, end.Compilation.Assembly))
                    {
                        continue;
                    }

                    foreach (var declaration in target.DeclaringSyntaxReferences)
                    {
                        if (declaration.GetSyntax() is not ClassDeclarationSyntax classDeclaration)
                        {
                            continue;
                        }

                        end.ReportDiagnostic(Diagnostic.Create(
                            MissingInfrastructureAttribute,
                            classDeclaration.Identifier.GetLocation(),
                            target.Name));
                        break;
                    }
                }
            });
        });
    }

    private static bool IsConcreteRegisterableClass(INamedTypeSymbol type)
    {
        return type.TypeKind == TypeKind.Class
            && !type.IsAbstract
            && !type.IsStatic;
    }

    private static bool HasInfrastructureAttribute(INamedTypeSymbol type)
    {
        return type.HasAttribute("InfrastructureAttribute");
    }

    private static bool HasRequisiteAttribute(ISymbol symbol)
    {
        return symbol.HasAttribute("RequisiteAttribute");
    }
}
