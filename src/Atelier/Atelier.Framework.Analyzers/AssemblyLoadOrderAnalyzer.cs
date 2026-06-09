using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atelier.Framework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AssemblyLoadOrderAnalyzer : DiagnosticAnalyzer
{
    private const string CATEGORY = "DependencyInjection";

    private static readonly DiagnosticDescriptor CrossAssemblyDependencyDiagnostic = new DiagnosticDescriptor(
        "ATELIER0603",
        "Cross-Assembly Requisite Dependency May Not Be Auto-Discovered",
        "Type '{0}' is in assembly '{1}' and may not be loaded during auto-discovery. Consider manual registration.",
        CATEGORY,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "When using [Requisite] dependencies from other assemblies, those assemblies must be loaded for auto-discovery to work. Consider explicit registration in startup code.",
        customTags: new[] { "CompilationEnd" });

    private static readonly DiagnosticDescriptor ActivatorUtilitiesBypassesDIDiagnostic = new DiagnosticDescriptor(
        "ATELIER0604",
        "ActivatorUtilities.CreateInstance Bypasses Auto-Discovery",
        "Type '{0}' is created with ActivatorUtilities but has [Requisite] dependencies that must be manually registered",
        CATEGORY,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "ActivatorUtilities.CreateInstance bypasses the DI container's auto-discovery. Ensure all [Requisite] dependencies are explicitly registered.",
        customTags: new[] { "CompilationEnd" });

    private static readonly DiagnosticDescriptor MissingAssemblyReferenceDiagnostic = new DiagnosticDescriptor(
        "ATELIER0605",
        "Assembly Reference Required for Auto-Discovery",
        "Assembly '{0}' contains [Infrastructure] types but is not referenced. Add as project reference or explicit assembly load.",
        CATEGORY,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Auto-discovery can only find types in loaded assemblies. Ensure all assemblies with [Infrastructure] types are referenced.",
        customTags: new[] { "CompilationEnd" });

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            CrossAssemblyDependencyDiagnostic,
            ActivatorUtilitiesBypassesDIDiagnostic,
            MissingAssemblyReferenceDiagnostic);

    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var crossAssemblyDeps = new ConcurrentDictionary<string, ConcurrentBag<CrossAssemblyDependency>>(StringComparer.Ordinal);
            var activatorUtilitiesUsages = new ConcurrentBag<ActivatorUtilitiesUsage>();

            compilationContext.RegisterSymbolAction(symbolContext =>
            {
                AnalyzeCrossAssemblyDependencies(symbolContext, crossAssemblyDeps);
            }, SymbolKind.NamedType);

            compilationContext.RegisterSyntaxNodeAction(syntaxContext =>
            {
                AnalyzeActivatorUtilitiesUsage(syntaxContext, activatorUtilitiesUsages);
            }, SyntaxKind.InvocationExpression);

            compilationContext.RegisterCompilationEndAction(endContext =>
            {
                ReportCrossAssemblyIssues(endContext, crossAssemblyDeps);
                ReportActivatorUtilitiesIssues(endContext, activatorUtilitiesUsages);
                ReportMissingAssemblyReferences(endContext);
            });
        });
    }

    private void ReportMissingAssemblyReferences(CompilationAnalysisContext context)
    {
        var compilation = context.Compilation;
        var reported = new HashSet<string>(StringComparer.Ordinal);

        var stack = new Stack<INamespaceSymbol>();
        stack.Push(compilation.Assembly.GlobalNamespace);

        while (stack.Count > 0)
        {
            var current = stack.Pop();

            foreach (var member in current.GetMembers())
            {
                if (member is INamespaceSymbol childNamespace)
                {
                    stack.Push(childNamespace);
                    continue;
                }

                if (member is not INamedTypeSymbol type
                    || !HasInfrastructureAttribute(type))
                {
                    continue;
                }

                ReportUnresolvedRequisiteFieldTypes(context, type, reported);
            }
        }
    }

    private static void ReportUnresolvedRequisiteFieldTypes(
        CompilationAnalysisContext context,
        INamedTypeSymbol type,
        HashSet<string> reported)
    {
        foreach (var field in type.GetMembers().OfType<IFieldSymbol>())
        {
            if (!HasRequisiteAttribute(field)
                || field.Type.TypeKind != TypeKind.Error)
            {
                continue;
            }

            var assemblyHint = field.Type.ContainingAssembly?.Name ?? field.Type.Name;
            if (!reported.Add(assemblyHint))
            {
                continue;
            }

            var syntaxRef = field.DeclaringSyntaxReferences.FirstOrDefault();
            var location = syntaxRef != null
                ? syntaxRef.GetSyntax().GetLocation()
                : Location.None;

            var diagnostic = Diagnostic.Create(
                MissingAssemblyReferenceDiagnostic,
                location,
                assemblyHint);
            context.ReportDiagnostic(diagnostic);
        }
    }

    private void AnalyzeCrossAssemblyDependencies(
        SymbolAnalysisContext context,
        ConcurrentDictionary<string, ConcurrentBag<CrossAssemblyDependency>> crossAssemblyDeps)
    {
        var typeSymbol = (INamedTypeSymbol)context.Symbol;

        if (!HasInfrastructureAttribute(typeSymbol))
        {
            return;
        }

        var currentAssembly = typeSymbol.ContainingAssembly?.Name;
        if (currentAssembly == null)
        {
            return;
        }

        var requisiteFields = typeSymbol.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(f => HasRequisiteAttribute(f));

        foreach (var field in requisiteFields)
        {
            var fieldType = field.Type;
            var fieldAssembly = fieldType.ContainingAssembly?.Name;
            if (fieldAssembly == null)
            {
                continue;
            }

            if (fieldAssembly != currentAssembly
                && IsAtelierAssembly(fieldAssembly))
            {
                var bucket = crossAssemblyDeps.GetOrAdd(currentAssembly, _ => new ConcurrentBag<CrossAssemblyDependency>());

                bucket.Add(new CrossAssemblyDependency
                {
                    DependentType = typeSymbol,
                    DependencyType = fieldType,
                    DependencyAssembly = fieldAssembly,
                    Field = field
                });
            }
        }
    }

    private void AnalyzeActivatorUtilitiesUsage(
        SyntaxNodeAnalysisContext context,
        ConcurrentBag<ActivatorUtilitiesUsage> activatorUtilitiesUsages)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return;
        }

        if (memberAccess.Expression is not IdentifierNameSyntax identifier ||
            identifier.Identifier.Text != "ActivatorUtilities")
        {
            return;
        }

        if (memberAccess.Name.Identifier.Text != "CreateInstance")
        {
            return;
        }

        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
        {
            return;
        }

        if (!methodSymbol.IsGenericMethod || methodSymbol.TypeArguments.Length == 0)
        {
            return;
        }

        var typeArgument = methodSymbol.TypeArguments[0];
        if (typeArgument is not INamedTypeSymbol namedType)
        {
            return;
        }

        var requisiteDependencies = namedType.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(f => HasRequisiteAttribute(f))
            .Select(f => f.Type)
            .ToList();

        if (requisiteDependencies.Count > 0)
        {
            activatorUtilitiesUsages.Add(new ActivatorUtilitiesUsage
            {
                InvocationSyntax = invocation,
                TargetType = namedType,
                RequisiteDependencies = requisiteDependencies
            });
        }
    }

    private void ReportCrossAssemblyIssues(
        CompilationAnalysisContext context,
        ConcurrentDictionary<string, ConcurrentBag<CrossAssemblyDependency>> crossAssemblyDeps)
    {
        foreach (var (assembly, dependencies) in crossAssemblyDeps)
        {
            var dependencyAssemblies = dependencies
                .Select(d => d.DependencyAssembly)
                .Distinct()
                .ToList();

            foreach (var dependency in dependencies)
            {
                if (!HasInfrastructureAttribute(dependency.DependencyType as INamedTypeSymbol))
                {
                    continue;
                }

                var syntaxRef = dependency.Field.DeclaringSyntaxReferences.FirstOrDefault();
                if (syntaxRef == null)
                {
                    continue;
                }

                var syntax = syntaxRef.GetSyntax();
                if (syntax is not VariableDeclaratorSyntax declarator)
                {
                    continue;
                }

                var diagnostic = Diagnostic.Create(
                    CrossAssemblyDependencyDiagnostic,
                    declarator.GetLocation(),
                    dependency.DependencyType.Name,
                    dependency.DependencyAssembly);

                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    private void ReportActivatorUtilitiesIssues(
        CompilationAnalysisContext context,
        ConcurrentBag<ActivatorUtilitiesUsage> activatorUtilitiesUsages)
    {
        foreach (var usage in activatorUtilitiesUsages)
        {
            var diagnostic = Diagnostic.Create(
                ActivatorUtilitiesBypassesDIDiagnostic,
                usage.InvocationSyntax.GetLocation(),
                usage.TargetType.Name);

            context.ReportDiagnostic(diagnostic);
        }
    }

    private static bool HasInfrastructureAttribute(INamedTypeSymbol? symbol)
    {
        return symbol.HasAttribute("InfrastructureAttribute");
    }

    private static bool HasRequisiteAttribute(ISymbol symbol)
    {
        return symbol.HasAttribute("RequisiteAttribute");
    }

    private static bool IsAtelierAssembly(string assemblyName)
    {
        return assemblyName.StartsWith("Atelier.", StringComparison.Ordinal);
    }

    private sealed class CrossAssemblyDependency
    {
        public INamedTypeSymbol DependentType { get; set; } = null!;
        public ITypeSymbol DependencyType { get; set; } = null!;
        public string DependencyAssembly { get; set; } = string.Empty;
        public IFieldSymbol Field { get; set; } = null!;
    }

    private sealed class ActivatorUtilitiesUsage
    {
        public InvocationExpressionSyntax InvocationSyntax { get; set; } = null!;
        public INamedTypeSymbol TargetType { get; set; } = null!;
        public List<ITypeSymbol> RequisiteDependencies { get; set; } = new();
    }
}
