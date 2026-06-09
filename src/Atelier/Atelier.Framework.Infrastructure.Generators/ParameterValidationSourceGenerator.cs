using Templar.Rendering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Atelier.Framework.Infrastructure.Generators;
using V = Atelier.Framework.Infrastructure.Generators.Templates.Validation;
using VV = Atelier.Framework.Infrastructure.Generators.Compositors.Validation.Validators;
using GT = Atelier.Framework.Infrastructure.Generators.Templates;

namespace Atelier.Framework.Compiler.Generators.Contract;

[Generator]
public sealed class ParameterValidationSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var validations = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => IsCandidate(node),
                static (ctx, _) => Transform(ctx))
            .Where(static result => result is not null)
            .Select(static (result, _) => result!);

        context.RegisterSourceOutput(
            validations,
            static (spc, result) =>
                spc.AddSource(
                    result.HintName,
                    SourceText.From(
                        result.Source,
                        System.Text.Encoding.UTF8)));
    }

    private static bool IsCandidate(SyntaxNode node)
    {
        if (node is not MethodDeclarationSyntax methodDeclaration)
        {
            return false;
        }

        return methodDeclaration.AttributeLists
            .SelectMany(al => al.Attributes)
            .Any(attr =>
            {
                var name = attr.Name.ToString();
                return name == "ValidatedMethod"
                    || name == "Operation"
                    || name == "OperationAttribute";
            });
    }

    private static ParameterValidationResult? Transform(GeneratorSyntaxContext ctx)
    {
        var methodDeclaration = (MethodDeclarationSyntax)ctx.Node;
        var methodSymbol = ctx.SemanticModel.GetDeclaredSymbol(methodDeclaration);

        if (methodSymbol == null || IsExcludedFromValidation(methodSymbol))
        {
            return null;
        }

        if (methodSymbol.ContainingType.TypeKind == TypeKind.Interface)
        {
            return null;
        }

        if (methodSymbol.IsGenericMethod)
        {
            return null;
        }

        if (!IsPartialClass(methodSymbol.ContainingType))
        {
            return null;
        }

        var parameters = methodSymbol.Parameters
            .Where(p => RequiresValidation(p))
            .ToList();

        if (parameters.Count == 0)
        {
            return null;
        }

        var validationCode = GenerateValidationCode(
            methodSymbol,
            parameters);

        var sanitizedMethodName = SanitizeMethodName(methodSymbol.Name);
        var fileName = $"{methodSymbol.ContainingType.Name}_{sanitizedMethodName}_{methodSymbol.Parameters.Length}_Validation.g.cs";
        return new ParameterValidationResult(fileName, validationCode);
    }

    private static bool IsExcludedFromValidation(IMethodSymbol methodSymbol)
    {
        var className = methodSymbol.ContainingType.Name;
        var namespaceName = methodSymbol.ContainingNamespace.ToDisplayString();

        if (namespaceName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase) ||
            namespaceName.Contains(".Tests.", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (methodSymbol.GetAttributes().Any(attr =>
            attr.AttributeClass?.Name == "NoValidationAttribute"))
        {
            return true;
        }

        if (methodSymbol.ContainingType.GetAttributes().Any(attr =>
            attr.AttributeClass?.Name == "NoValidationAttribute"))
        {
            return true;
        }

        if (methodSymbol.IsOverride || methodSymbol.IsVirtual)
        {
            return true;
        }

        return false;
    }

    private static bool RequiresValidation(IParameterSymbol parameter)
    {
        if (parameter.RefKind == RefKind.Out || parameter.RefKind == RefKind.Ref)
        {
            return false;
        }

        if (parameter.IsOptional || parameter.IsParams)
        {
            return false;
        }

        if (parameter.Type.IsValueType &&
            parameter.NullableAnnotation != NullableAnnotation.Annotated)
        {
            return false;
        }

        if (parameter.NullableAnnotation == NullableAnnotation.Annotated)
        {
            return false;
        }

        return parameter.Type.SpecialType == SpecialType.System_String ||
               parameter.Type.TypeKind == TypeKind.Class ||
               parameter.Type.TypeKind == TypeKind.Interface ||
               parameter.Type.TypeKind == TypeKind.Delegate ||
               parameter.Type.TypeKind == TypeKind.Array;
    }

    private static Compositor? BuildOutcomeFailureExpression(IMethodSymbol method)
    {
        var returnType = method.ReturnType;

        ITypeSymbol? unwrapped = returnType;
        var name = (returnType as INamedTypeSymbol)?.Name;
        if (name is "Task" or "ValueTask")
        {
            var named = (INamedTypeSymbol)returnType;
            unwrapped = named.IsGenericType ? named.TypeArguments[0] : null;
        }

        if (unwrapped is null)
        {
            return null;
        }

        var unwrappedNamed = unwrapped as INamedTypeSymbol;
        if (unwrappedNamed?.Name != "Outcome")
        {
            return null;
        }

        if (!unwrappedNamed.IsGenericType)
        {
            return new V.OutcomeFailureExpr();
        }

        var t = unwrappedNamed.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return new V.OutcomeFailureExprWithType
        {
            TypeArg = t,
        };
    }

    private static bool IsAsyncReturn(IMethodSymbol method)
    {
        var name = (method.ReturnType as INamedTypeSymbol)?.Name;
        return name is "Task" or "ValueTask";
    }

    private static readonly SymbolDisplayFormat FQ_FORMAT = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static string GenerateValidationCode(IMethodSymbol methodSymbol, List<IParameterSymbol> parameters)
    {
        var containingType = methodSymbol.ContainingType;
        var className = containingType.Name;

        var typeParamList = Sequence.CommaList(containingType.TypeParameters.Select(tp => (Compositor)new GT.IdentFragment { Text = tp.Name })).Render();
        var typeParams = containingType.IsGenericType ? "<" + typeParamList + ">" : string.Empty;

        var methodName = methodSymbol.Name;
        var namespaceName = containingType.ContainingNamespace.ToDisplayString();
        var returnType = methodSymbol.ReturnType.SpecialType == SpecialType.System_Void
            ? "void"
            : methodSymbol.ReturnType.ToDisplayString(FQ_FORMAT);

        var validations = Sequence.Lines(parameters.Select(p => (Compositor)BuildValidationStatement(methodSymbol, p)));

        var parameterList = Sequence.CommaList(methodSymbol.Parameters.Select(p => (Compositor)new GT.ParameterFragment
        {
            ParamType = RefPrefix(p) + p.Type.ToDisplayString(FQ_FORMAT),
            ParamName = p.Name,
            DefaultClause = p.HasExplicitDefaultValue ? " = " + ParameterFormatting.FormatDefaultValue(p) : string.Empty,
        })).Render();

        var argumentList = Sequence.CommaList(methodSymbol.Parameters.Select(p => (Compositor)new V.ArgumentRef
        {
            RefModifier = RefPrefix(p),
            ParamName = p.Name,
        })).Render();

        var returnKeyword = methodSymbol.ReturnType.SpecialType == SpecialType.System_Void ? string.Empty : "return ";

        var returnStatement = new V.MethodCall
        {
            ReturnKeyword = returnKeyword,
            MethodName = methodName,
            ArgumentList = argumentList,
        };

        return new V.Wrapper
        {
            Usings = string.Empty,
            NamespaceName = namespaceName,
            ClassName = className,
            TypeParams = typeParams,
            ReturnType = returnType,
            MethodName = methodName,
            ParameterList = parameterList,
            ParameterValidations = validations,
            ReturnStatement = returnStatement,
        }.Render();
    }

    private static VV.ValidationStatement BuildValidationStatement(IMethodSymbol method, IParameterSymbol p)
    {
        var failureExpr = BuildOutcomeFailureExpression(method);
        if (failureExpr is null)
        {
            return new VV.ThrowOnNullValidator { ParamName = p.Name };
        }

        if (IsAsyncReturn(method))
        {
            var taskName = ((INamedTypeSymbol)method.ReturnType).Name == "ValueTask"
                ? "global::System.Threading.Tasks.ValueTask"
                : "global::System.Threading.Tasks.Task";

            return new VV.OutcomeFailureAsyncValidator
            {
                ParamName = p.Name,
                TaskTypeName = taskName,
                OutcomeExpression = failureExpr.Render(),
            };
        }

        return new VV.OutcomeFailureSyncValidator
        {
            ParamName = p.Name,
            OutcomeExpression = failureExpr.Render(),
        };
    }

    private static string RefPrefix(IParameterSymbol p)
    {
        return p.RefKind switch
        {
            RefKind.Out => "out ",
            RefKind.Ref => "ref ",
            _ => string.Empty,
        };
    }

    private static bool IsPartialClass(INamedTypeSymbol type)
    {
        foreach (var syntaxRef in type.DeclaringSyntaxReferences)
        {
            if (syntaxRef.GetSyntax() is ClassDeclarationSyntax cls
                && cls.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
            {
                return true;
            }
        }
        return false;
    }

    private static string SanitizeMethodName(string methodName)
    {
        var chars = new char[methodName.Length];
        for (var i = 0; i < methodName.Length; i++)
        {
            chars[i] = char.IsLetterOrDigit(methodName[i]) ? methodName[i] : '_';
        }
        return new string(chars);
    }
}

internal sealed record ParameterValidationResult(string HintName, string Source);
