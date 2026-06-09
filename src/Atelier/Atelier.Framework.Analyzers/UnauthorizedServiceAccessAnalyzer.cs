using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atelier.Framework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnauthorizedServiceAccessAnalyzer : DiagnosticAnalyzer
{
    public const string DIAGNOSTIC_ID = "ATELIER0330";

    private static readonly DiagnosticDescriptor UndeclaredServiceDependency = new DiagnosticDescriptor(
        DIAGNOSTIC_ID,
        "Undeclared service dependency",
        "Service '{0}' holds a handle to service '{1}' that is not a declared [Requisite] dependency. Service-to-service access is authorized only by [Requisite]; obtain it through [Requisite] injection.",
        "Network",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Under needs-based isolation, the only authorized way for a service to reach another service is an explicit [Requisite] declaration. A service-typed field or property without [Requisite] is an undeclared (unauthorized) service-to-service edge.",
        customTags: new[] { "CompilationEnd" });

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(UndeclaredServiceDependency);

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
            var contracts = new ConcurrentDictionary<INamedTypeSymbol, byte>(SymbolEqualityComparer.Default);
            var services = new ConcurrentBag<INamedTypeSymbol>();

            start.RegisterSymbolAction(symbolContext =>
            {
                var type = (INamedTypeSymbol)symbolContext.Symbol;
                if (!IsConcreteService(type))
                {
                    return;
                }

                services.Add(type);
                foreach (var contract in type.AllInterfaces)
                {
                    if (IsServiceContractInterface(contract))
                    {
                        contracts.TryAdd(contract, 0);
                    }
                }
            }, SymbolKind.NamedType);

            start.RegisterCompilationEndAction(end =>
            {
                var contractSet = new HashSet<INamedTypeSymbol>(contracts.Keys, SymbolEqualityComparer.Default);
                foreach (var service in services)
                {
                    if (IsTestType(service))
                    {
                        continue;
                    }

                    AnalyzeServiceFields(end, service, contractSet);
                }
            });
        });
    }

    private static void AnalyzeServiceFields(
        CompilationAnalysisContext context,
        INamedTypeSymbol service,
        HashSet<INamedTypeSymbol> contracts)
    {
        var authorized = BuildAuthorizedSet(service);

        foreach (var member in service.GetMembers())
        {
            ITypeSymbol? heldType = member switch
            {
                IFieldSymbol field when !field.IsStatic && !field.IsConst && !field.IsImplicitlyDeclared => field.Type,
                IPropertySymbol property when !property.IsStatic => property.Type,
                _ => null
            };

            if (heldType is not INamedTypeSymbol namedHeld)
            {
                continue;
            }

            if (!IsGatedServiceType(namedHeld, contracts))
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(namedHeld, service)
                || authorized.Contains(namedHeld))
            {
                continue;
            }

            var location = member.Locations.FirstOrDefault();
            if (location == null)
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                UndeclaredServiceDependency,
                location,
                service.Name,
                namedHeld.Name));
        }
    }

    private static HashSet<INamedTypeSymbol> BuildAuthorizedSet(INamedTypeSymbol service)
    {
        var authorized = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var member in service.GetMembers())
        {
            if (!HasRequisiteAttribute(member))
            {
                continue;
            }

            ITypeSymbol? requisiteType = member switch
            {
                IFieldSymbol field => field.Type,
                IPropertySymbol property => property.Type,
                _ => null
            };

            if (requisiteType is INamedTypeSymbol named)
            {
                authorized.Add(named);
            }
        }

        return authorized;
    }

    private static bool IsGatedServiceType(INamedTypeSymbol type, HashSet<INamedTypeSymbol> contracts)
    {
        if (type.TypeKind == TypeKind.Interface)
        {
            return contracts.Contains(type);
        }

        return IsConcreteService(type);
    }

    private static bool IsConcreteService(INamedTypeSymbol type)
    {
        if (type.TypeKind != TypeKind.Class
            || type.IsAbstract)
        {
            return false;
        }

        return type.HasAttribute("InfrastructureAttribute")
            || type.HasAttribute("ServiceDiscoveryAttribute");
    }

    private static bool IsServiceContractInterface(INamedTypeSymbol contract)
    {
        if (contract.TypeKind != TypeKind.Interface)
        {
            return false;
        }

        var name = contract.Name;
        if (name == "IDisposable"
            || name == "IAtelier"
            || name == "IInterceptor"
            || name == "IAsyncDisposable")
        {
            return false;
        }

        var ns = contract.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return ns.StartsWith("Atelier", System.StringComparison.Ordinal);
    }

    private static bool HasRequisiteAttribute(ISymbol symbol)
    {
        return symbol.HasAttribute("RequisiteAttribute");
    }

    private static bool IsTestType(INamedTypeSymbol type)
    {
        var ns = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (ns.EndsWith(".Tests", System.StringComparison.OrdinalIgnoreCase)
            || ns.Contains(".Tests.", System.StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return type.GetMembers()
            .OfType<IMethodSymbol>()
            .Any(m => m.HasAttribute("GeneratedTestAttribute"));
    }
}
