using System.Collections.Immutable;
using Templar.Rendering;
using Templar.Presets;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using G = Atelier.Framework.Infrastructure.Generators.Templates.Operation;
using F = Atelier.Framework.Infrastructure.Generators.Compositors.Operation.FailureModes;
using GT = Atelier.Framework.Infrastructure.Generators.Templates;

namespace Atelier.Framework.Infrastructure.Generators;

[Generator]
public sealed class OperationSourceGenerator : IIncrementalGenerator
{
    private static readonly SymbolDisplayFormat FullyQualifiedFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters | SymbolDisplayGenericsOptions.IncludeTypeConstraints,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var operations = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => IsCandidate(node),
                static (ctx, _) => Transform(ctx))
            .Where(static result => result is not null)
            .Select(static (result, _) => result!)
            .Collect();

        var combined = operations.Combine(context.CompilationProvider);

        context.RegisterSourceOutput(
            combined,
            static (spc, pair) => Emit(spc, pair.Left, pair.Right));
    }

    private static bool IsCandidate(SyntaxNode node)
    {
        if (node is not MethodDeclarationSyntax methodDeclaration)
        {
            return false;
        }

        return methodDeclaration.AttributeLists
            .SelectMany(al => al.Attributes)
            .Any(a => a.Name.ToString() is "Operation" or "OperationAttribute");
    }

    private static OperationMethodResult? Transform(GeneratorSyntaxContext ctx)
    {
        var methodDeclaration = (MethodDeclarationSyntax)ctx.Node;
        var symbol = ctx.SemanticModel.GetDeclaredSymbol(methodDeclaration);

        if (symbol is not IMethodSymbol methodSymbol)
        {
            return null;
        }

        var operationAttribute = GetOperationAttribute(methodSymbol);
        if (operationAttribute == null)
        {
            return null;
        }

        if (methodSymbol.IsGenericMethod)
        {
            return null;
        }
        var owner = methodSymbol.ContainingType;
        if (owner.IsGenericType)
        {
            return null;
        }
        if (!owner.AllInterfaces.Any(i => i.Name == "IAtelier"))
        {
            return null;
        }

        var containingClass = methodSymbol.ContainingType;
        if (containingClass == null)
        {
            return null;
        }

        var operationName = operationAttribute.ConstructorArguments.FirstOrDefault().Value?.ToString()
            ?? operationAttribute.NamedArguments
                .FirstOrDefault(kvp => kvp.Key == "Name").Value.Value?.ToString()
            ?? methodSymbol.Name;

        return new OperationMethodResult(containingClass, methodSymbol, operationName);
    }

    private static void Emit(
        SourceProductionContext context,
        ImmutableArray<OperationMethodResult> results,
        Compilation compilation)
    {
        var wellKnown = new WellKnownReturnTypes(compilation);

        var methodsByClass = new Dictionary<INamedTypeSymbol, List<(IMethodSymbol Method, string OperationName)>>(SymbolEqualityComparer.Default);

        foreach (var result in results)
        {
            if (!methodsByClass.TryGetValue(result.ContainingClass, out var methods))
            {
                methods = new List<(IMethodSymbol, string)>();
                methodsByClass[result.ContainingClass] = methods;
            }

            methods.Add((result.Method, result.OperationName));
        }

        foreach (var (classSymbol, methods) in methodsByClass)
        {
            methods.Sort(static (left, right) =>
            {
                var byName = string.CompareOrdinal(left.OperationName, right.OperationName);
                if (byName != 0)
                {
                    return byName;
                }
                var byMethod = string.CompareOrdinal(left.Method.Name, right.Method.Name);
                if (byMethod != 0)
                {
                    return byMethod;
                }
                return string.CompareOrdinal(SignatureKey(left.Method), SignatureKey(right.Method));
            });

            var generatedCode = GenerateOperationWrappers(classSymbol, methods, wellKnown);
            var namespacePart = classSymbol.ContainingNamespace.ToDisplayString().Replace(".", "_");
            var fileName = $"{namespacePart}_{classSymbol.Name}_Operations.g.cs";
            context.AddSource(fileName, SourceText.From(generatedCode, System.Text.Encoding.UTF8));
        }
    }

    private static AttributeData? GetOperationAttribute(IMethodSymbol methodSymbol)
    {
        return methodSymbol.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.Name == "OperationAttribute");
    }

    private static string SignatureKey(IMethodSymbol method)
    {
        return string.Join(",",
                           method.Parameters.Select(p => p.Type.ToDisplayString(FullyQualifiedFormat)));
    }

    private static string GenerateOperationWrappers(INamedTypeSymbol classSymbol,
                                             List<(IMethodSymbol Method, string OperationName)> methods,
                                             WellKnownReturnTypes wellKnown)
    {
        var namespaceName = classSymbol.ContainingNamespace.ToDisplayString();
        var className = classSymbol.Name;

        var wrappers = Sequence.Lines(methods.Select(m => (Compositor)BuildWrapper(m.Method, m.OperationName, wellKnown)));

        var body = new G.Body
        {
            ClassName = className,
            Wrappers = wrappers,
        };

        return new CSharpFile
        {
            Namespace = namespaceName,
            Usings = new[]
            {
                "global::System",
                "global::System.Threading",
                "global::System.Threading.Tasks",
                "global::Atelier.Framework.Outcomes",
                "global::Atelier.Framework.Observability",
            },
            Body = body.Render(),
        }.Render();
    }

    private static Compositor BuildWrapper(IMethodSymbol method, string operationName, WellKnownReturnTypes wellKnown)
    {
        var methodName = method.Name;
        var wrapperName = methodName + "_Traced";
        var isVoid = method.ReturnsVoid;
        var returnType = isVoid ? "void" : method.ReturnType.ToDisplayString(FullyQualifiedFormat);
        var isAsync = IsAsyncMethod(method, wellKnown);
        var hasOutcome = HasOutcomeReturn(method, wellKnown);

        var typeParamList = Sequence.CommaList(method.TypeParameters.Select(tp => (Compositor)new GT.IdentFragment { Text = tp.Name })).Render();
        var typeParams = method.IsGenericMethod ? "<" + typeParamList + ">" : string.Empty;
        var typeArgs = method.IsGenericMethod ? "<" + typeParamList + ">" : string.Empty;

        var parameterList = Sequence.CommaList(method.Parameters.Select(p => (Compositor)BuildParameter(p))).Render();

        var argumentList = Sequence.CommaList(method.Parameters.Select(p => (Compositor)new GT.IdentFragment { Text = p.Name })).Render();

        var asyncKeyword = isAsync ? "async " : string.Empty;
        var awaitKeyword = isAsync ? "await " : string.Empty;

        Compositor failureReturn = SelectFailureMode(method, isVoid, hasOutcome, wellKnown);

        var returnsTaskNoValue = isAsync
            && method.ReturnType is INamedTypeSymbol rNamed
            && !rNamed.IsGenericType
            && (wellKnown.IsTask(rNamed)
                || wellKnown.IsValueTask(rNamed));
        var returnKeyword = (isVoid || returnsTaskNoValue) ? string.Empty : "return ";

        return new G.Wrapper
        {
            MethodName = methodName,
            OperationName = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(operationName, quote: true),
            AsyncKeyword = asyncKeyword,
            ReturnType = returnType,
            WrapperName = wrapperName,
            TypeParams = typeParams,
            TypeArgs = typeArgs,
            ParameterList = parameterList,
            AwaitKeyword = awaitKeyword,
            ArgumentList = argumentList,
            FailureReturn = failureReturn,
            ReturnKeyword = returnKeyword,
        };
    }

    private static Compositor BuildParameter(IParameterSymbol param)
    {
        var paramType = param.Type.ToDisplayString(FullyQualifiedFormat);
        var paramName = param.Name;
        return new GT.ParameterFragment
        {
            ParamType = paramType,
            ParamName = paramName,
            DefaultClause = param.HasExplicitDefaultValue ? " = " + ParameterFormatting.FormatDefaultValue(param) : string.Empty,
        };
    }

    private static Compositor SelectFailureMode(IMethodSymbol method, bool isVoid, bool hasOutcome, WellKnownReturnTypes wellKnown)
    {
        if (hasOutcome)
        {
            var outcomeType = GetOutcomeType(method, wellKnown);
            if (outcomeType != null)
            {
                return new F.OutcomeFailureWithType { TypeArg = outcomeType };
            }
            return new F.OutcomeFailureBare();
        }
        if (isVoid)
        {
            return new F.VoidReturn();
        }
        return new F.Rethrow();
    }

    private static bool IsAsyncMethod(IMethodSymbol method, WellKnownReturnTypes wellKnown)
    {
        if (method.ReturnType is not INamedTypeSymbol named)
        {
            return false;
        }
        return wellKnown.IsTask(named)
            || wellKnown.IsValueTask(named);
    }

    private static bool HasOutcomeReturn(IMethodSymbol method, WellKnownReturnTypes wellKnown)
    {
        return wellKnown.IsOutcomeReturn(method.ReturnType);
    }

    private static string? GetOutcomeType(IMethodSymbol method, WellKnownReturnTypes wellKnown)
    {
        var inner = wellKnown.GetOutcomeTypeArgument(method.ReturnType);
        return inner?.ToDisplayString(FullyQualifiedFormat);
    }
}

internal sealed class WellKnownReturnTypes
{
    private readonly INamedTypeSymbol? _task;
    private readonly INamedTypeSymbol? _taskOfT;
    private readonly INamedTypeSymbol? _valueTask;
    private readonly INamedTypeSymbol? _valueTaskOfT;
    private readonly INamedTypeSymbol? _outcome;
    private readonly INamedTypeSymbol? _outcomeOfT;

    public WellKnownReturnTypes(Compilation compilation)
    {
        _task = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task");
        _taskOfT = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1");
        _valueTask = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask");
        _valueTaskOfT = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1");
        _outcome = compilation.GetTypeByMetadataName("Atelier.Framework.Outcomes.Outcome");
        _outcomeOfT = compilation.GetTypeByMetadataName("Atelier.Framework.Outcomes.Outcome`1");
    }

    public bool IsTask(INamedTypeSymbol type)
    {
        return Matches(type, _task)
            || Matches(type.OriginalDefinition, _taskOfT);
    }

    public bool IsValueTask(INamedTypeSymbol type)
    {
        return Matches(type, _valueTask)
            || Matches(type.OriginalDefinition, _valueTaskOfT);
    }

    public bool IsOutcomeReturn(ITypeSymbol returnType)
    {
        var unwrapped = Unwrap(returnType);
        if (unwrapped is not INamedTypeSymbol named)
        {
            return false;
        }
        return Matches(named, _outcome)
            || Matches(named.OriginalDefinition, _outcomeOfT);
    }

    public ITypeSymbol? GetOutcomeTypeArgument(ITypeSymbol returnType)
    {
        var unwrapped = Unwrap(returnType);
        if (unwrapped is INamedTypeSymbol named
            && Matches(named.OriginalDefinition, _outcomeOfT))
        {
            return named.TypeArguments.FirstOrDefault();
        }
        return null;
    }

    private ITypeSymbol Unwrap(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named
            && named.IsGenericType
            && (Matches(named.OriginalDefinition, _taskOfT)
                || Matches(named.OriginalDefinition, _valueTaskOfT)))
        {
            return named.TypeArguments.FirstOrDefault() ?? type;
        }
        return type;
    }

    private static bool Matches(ITypeSymbol candidate, INamedTypeSymbol? known)
    {
        return known is not null
            && SymbolEqualityComparer.Default.Equals(candidate, known);
    }
}

internal sealed record OperationMethodResult(
    INamedTypeSymbol ContainingClass,
    IMethodSymbol Method,
    string OperationName);
