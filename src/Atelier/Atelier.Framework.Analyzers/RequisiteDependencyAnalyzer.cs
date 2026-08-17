using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Atelier.Framework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RequisiteDependencyAnalyzer : DiagnosticAnalyzer
{
    private const string CATEGORY = "DependencyInjection";

    private static readonly DiagnosticDescriptor MissingRegistrationDiagnostic = new DiagnosticDescriptor(
        "ATELIER0600",
        "Missing DI Registration for Requisite Dependency",
        "Type '{0}' is used as [Requisite] dependency but is not registered with [Infrastructure] attribute",
        CATEGORY,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "All types used with [Requisite] attribute must be registered in DI container via [Infrastructure] attribute or manual registration.",
        customTags: new[] { "CompilationEnd" });

    private static readonly DiagnosticDescriptor ConstructorDependencyNotRegisteredDiagnostic = new DiagnosticDescriptor(
        "ATELIER0601",
        "Missing DI Registration for Constructor Dependency",
        "Type '{0}' is used as constructor dependency but is not registered with [Infrastructure] attribute",
        CATEGORY,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "All types used as constructor parameters must be registered in DI container via [Infrastructure] attribute or manual registration.",
        customTags: new[] { "CompilationEnd" });

    private static readonly DiagnosticDescriptor ManualRegistrationHintDiagnostic = new DiagnosticDescriptor(
        "ATELIER0602",
        "Consider Adding [Infrastructure] Attribute",
        "Type '{0}' is manually registered but could benefit from [Infrastructure] attribute for automatic registration",
        CATEGORY,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Types used in DI can use [Infrastructure] attribute for automatic registration and validation.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            MissingRegistrationDiagnostic,
            ConstructorDependencyNotRegisteredDiagnostic,
            ManualRegistrationHintDiagnostic);

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
            var registeredTypes = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
            var manualRegistrations = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
            var candidateTypes = new ConcurrentBag<INamedTypeSymbol>();

            CollectReferencedRegisteredTypes(compilationContext.Compilation, registeredTypes);

            compilationContext.RegisterSymbolAction(symbolContext =>
            {
                var typeSymbol = (INamedTypeSymbol)symbolContext.Symbol;

                CollectRegisteredTypes(typeSymbol, registeredTypes);

                if (typeSymbol.TypeKind == TypeKind.Class
                    && (HasInfrastructureAttribute(typeSymbol)
                        || typeSymbol.GetMembers().OfType<IFieldSymbol>().Any(HasRequisiteAttribute)))
                {
                    candidateTypes.Add(typeSymbol);
                }
            }, SymbolKind.NamedType);

            compilationContext.RegisterOperationAction(operationContext =>
            {
                CollectManualRegistrations((IInvocationOperation)operationContext.Operation, manualRegistrations);
            }, OperationKind.Invocation);

            compilationContext.RegisterCompilationEndAction(endContext =>
            {
                ValidateDependencies(endContext, candidateTypes, registeredTypes, manualRegistrations);
            });
        });
    }

    private void CollectRegisteredTypes(
        INamedTypeSymbol typeSymbol,
        ConcurrentDictionary<string, byte> registeredTypes)
    {
        if (HasInfrastructureAttribute(typeSymbol))
        {
            var fullName = GetFullTypeName(typeSymbol);
            registeredTypes.TryAdd(fullName, 0);

            foreach (var @interface in typeSymbol.AllInterfaces)
            {
                registeredTypes.TryAdd(GetFullTypeName(@interface), 0);
            }
        }
    }

    private void CollectReferencedRegisteredTypes(
        Compilation compilation,
        ConcurrentDictionary<string, byte> registeredTypes)
    {
        foreach (var reference in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (!reference.Name.StartsWith("Atelier.", StringComparison.Ordinal))
            {
                continue;
            }

            var stack = new Stack<INamespaceSymbol>();
            stack.Push(reference.GlobalNamespace);

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

                    if (member is INamedTypeSymbol typeSymbol)
                    {
                        CollectRegisteredTypes(typeSymbol, registeredTypes);
                    }
                }
            }
        }
    }

    private void CollectManualRegistrations(
        IInvocationOperation invocation,
        ConcurrentDictionary<string, byte> manualRegistrations)
    {
        var targetMethod = invocation.TargetMethod;
        var methodName = targetMethod.Name;

        var isRegistration = methodName is "AddSingleton" or "AddScoped" or "AddTransient"
            || (methodName == "CreateInstance"
                && targetMethod.ContainingType?.Name == "ActivatorUtilities");

        if (!isRegistration)
        {
            return;
        }

        foreach (var typeArgument in targetMethod.TypeArguments)
        {
            if (typeArgument.TypeKind != TypeKind.Error)
            {
                manualRegistrations.TryAdd(GetFullTypeName(typeArgument), 0);
            }
        }
    }

    private void ValidateDependencies(
        CompilationAnalysisContext context,
        ConcurrentBag<INamedTypeSymbol> candidateTypes,
        ConcurrentDictionary<string, byte> registeredTypes,
        ConcurrentDictionary<string, byte> manualRegistrations)
    {
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var typeSymbol in candidateTypes)
        {
            if (!seen.Add(typeSymbol))
            {
                continue;
            }

            ValidateRequisiteDependencies(context, typeSymbol, registeredTypes, manualRegistrations);
            ValidateConstructorDependencies(context, typeSymbol, registeredTypes, manualRegistrations);
        }
    }

    private void ValidateRequisiteDependencies(
        CompilationAnalysisContext context,
        INamedTypeSymbol typeSymbol,
        ConcurrentDictionary<string, byte> registeredTypes,
        ConcurrentDictionary<string, byte> manualRegistrations)
    {
        var fields = typeSymbol.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(f => HasRequisiteAttribute(f));

        foreach (var field in fields)
        {
            if (IsOptionalRequisite(field))
            {
                continue;
            }

            var fieldType = field.Type;
            var fullTypeName = GetFullTypeName(fieldType);

            if (!IsRegistered(fullTypeName, fieldType, registeredTypes, manualRegistrations))
            {
                ReportMissingRegistration(context, field, fullTypeName);
            }
        }
    }

    private void ValidateConstructorDependencies(
        CompilationAnalysisContext context,
        INamedTypeSymbol typeSymbol,
        ConcurrentDictionary<string, byte> registeredTypes,
        ConcurrentDictionary<string, byte> manualRegistrations)
    {
        if (!HasInfrastructureAttribute(typeSymbol))
        {
            return;
        }

        foreach (var constructor in typeSymbol.Constructors)
        {
            if (constructor.IsStatic || constructor.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            foreach (var parameter in constructor.Parameters)
            {
                var parameterType = parameter.Type;
                var fullTypeName = GetFullTypeName(parameterType);

                if (!IsRegistered(fullTypeName, parameterType, registeredTypes, manualRegistrations) &&
                    !IsFrameworkType(parameterType))
                {
                    ReportConstructorDependencyMissing(context, parameter, fullTypeName);
                }
            }
        }
    }

    private bool IsRegistered(
        string fullTypeName,
        ITypeSymbol typeSymbol,
        ConcurrentDictionary<string, byte> registeredTypes,
        ConcurrentDictionary<string, byte> manualRegistrations)
    {
        if (registeredTypes.ContainsKey(fullTypeName))
        {
            return true;
        }

        if (manualRegistrations.ContainsKey(fullTypeName))
        {
            return true;
        }

        if (IsFrameworkType(typeSymbol))
        {
            return true;
        }

        if (typeSymbol is INamedTypeSymbol namedType && namedType.IsGenericType)
        {
            var genericDefinition = namedType.ConstructedFrom;
            var genericFullName = GetFullTypeName(genericDefinition);
            if (registeredTypes.ContainsKey(genericFullName)
                || manualRegistrations.ContainsKey(genericFullName))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsFrameworkType(ITypeSymbol typeSymbol)
    {
        var fullName = GetFullTypeName(typeSymbol);

        return fullName.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal)
               || fullName.StartsWith("System.", StringComparison.Ordinal)
               || fullName == "Microsoft.Extensions.Logging.ILogger"
               || fullName == "Microsoft.Extensions.Options.IOptions"
               || fullName == "Microsoft.Extensions.Configuration.IConfiguration"
               || IsHostSuppliedContract(fullName);
    }

    private static bool IsHostSuppliedContract(string fullName)
    {
        return fullName == "Atelier.Framework.Observability.ILogger"
               || fullName == "Atelier.Framework.EventStream.Orchestration.IEventStreamManager"
               || fullName == "Atelier.Framework.EventStream.Consumers.IEventStreamConsumer"
               || fullName == "Atelier.Framework.StateMachine.Service.IStateMachinePersistence";
    }

    private void ReportMissingRegistration(
        CompilationAnalysisContext context,
        IFieldSymbol field,
        string typeName)
    {
        var syntaxRef = field.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxRef == null)
        {
            return;
        }

        var syntax = syntaxRef.GetSyntax();
        if (syntax is not VariableDeclaratorSyntax declarator)
        {
            return;
        }

        var diagnostic = Diagnostic.Create(
            MissingRegistrationDiagnostic,
            declarator.GetLocation(),
            typeName);

        context.ReportDiagnostic(diagnostic);
    }

    private void ReportConstructorDependencyMissing(
        CompilationAnalysisContext context,
        IParameterSymbol parameter,
        string typeName)
    {
        var syntaxRef = parameter.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxRef == null)
        {
            return;
        }

        var syntax = syntaxRef.GetSyntax();

        var diagnostic = Diagnostic.Create(
            ConstructorDependencyNotRegisteredDiagnostic,
            syntax.GetLocation(),
            typeName);

        context.ReportDiagnostic(diagnostic);
    }

    private static bool HasInfrastructureAttribute(ISymbol symbol)
    {
        return symbol.HasAttribute("InfrastructureAttribute");
    }

    private static bool HasRequisiteAttribute(ISymbol symbol)
    {
        return symbol.HasAttribute("RequisiteAttribute");
    }

    private static bool IsOptionalRequisite(ISymbol symbol)
    {
        var requisite = symbol.FindAttribute("RequisiteAttribute");

        if (requisite is null)
        {
            return false;
        }

        foreach (var named in requisite.NamedArguments)
        {
            if (named.Key == "Required"
                && named.Value.Value is false)
            {
                return true;
            }
        }

        return false;
    }

    private static string GetFullTypeName(ITypeSymbol typeSymbol)
    {
        if (typeSymbol is INamedTypeSymbol namedType && namedType.IsGenericType)
        {
            var genericPart = namedType.ConstructedFrom.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat
                    .WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted));
            return genericPart;
        }

        return typeSymbol.ToDisplayString(
            SymbolDisplayFormat.FullyQualifiedFormat
                .WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted));
    }
}
