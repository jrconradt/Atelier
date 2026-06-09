using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atelier.Framework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DisposeWithoutDisposableInterfaceAnalyzer : DiagnosticAnalyzer
{
    private const string CATEGORY = "Infrastructure";

    private static readonly DiagnosticDescriptor DisposeWithoutInterfaceDiagnostic = new DiagnosticDescriptor(
        "ATELIER0403",
        "Dispose Method Without Disposable Interface",
        "Type '{0}' declares a '{1}' method but does not implement IDisposable or IAsyncDisposable, so the container never invokes it",
        CATEGORY,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A Dispose or DisposeAsync body on a type that declares neither IDisposable nor IAsyncDisposable is dead code; the DI container only disposes types whose declaration carries the interface.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DisposeWithoutInterfaceDiagnostic);

    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        if (type.TypeKind != TypeKind.Class
            && type.TypeKind != TypeKind.Struct)
        {
            return;
        }

        if (ImplementsDisposable(type))
        {
            return;
        }

        foreach (var member in type.GetMembers())
        {
            if (member is not IMethodSymbol method)
            {
                continue;
            }

            if (!IsDisposeShape(method))
            {
                continue;
            }

            var location = method.Locations.FirstOrDefault(l => l.IsInSource);
            if (location == null)
            {
                continue;
            }

            var diagnostic = Diagnostic.Create(
                DisposeWithoutInterfaceDiagnostic,
                location,
                type.Name,
                method.Name);
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static bool ImplementsDisposable(INamedTypeSymbol type)
    {
        foreach (var iface in type.AllInterfaces)
        {
            if (iface.SpecialType == SpecialType.System_IDisposable)
            {
                return true;
            }

            if (iface.Name == "IAsyncDisposable"
                && iface.ContainingNamespace?.ToDisplayString() == "System")
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDisposeShape(IMethodSymbol method)
    {
        if (method.MethodKind != MethodKind.Ordinary)
        {
            return false;
        }

        if (method.DeclaredAccessibility != Accessibility.Public)
        {
            return false;
        }

        if (method.Parameters.Length != 0)
        {
            return false;
        }

        if (method.Name == "Dispose"
            && method.ReturnsVoid)
        {
            return true;
        }

        if (method.Name == "DisposeAsync"
            && IsValueTaskReturn(method.ReturnType))
        {
            return true;
        }

        return false;
    }

    private static bool IsValueTaskReturn(ITypeSymbol returnType)
    {
        var name = returnType.Name;
        if (name != "ValueTask"
            && name != "Task")
        {
            return false;
        }

        return returnType.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks";
    }
}
