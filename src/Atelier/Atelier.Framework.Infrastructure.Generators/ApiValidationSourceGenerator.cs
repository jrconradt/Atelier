using Templar.Rendering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using GA = Atelier.Framework.Infrastructure.Generators.Templates.Api;
using V = Atelier.Framework.Infrastructure.Generators.Compositors.Api.Validators;
using GT = Atelier.Framework.Infrastructure.Generators.Templates;

namespace Atelier.Framework.Infrastructure.Generators;

[Generator]
public sealed class ApiValidationSourceGenerator : IIncrementalGenerator
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
                spc.AddSource(result.HintName,
                              SourceText.From(result.Source, System.Text.Encoding.UTF8)));
    }

    private static bool IsCandidate(SyntaxNode node)
    {
        if (node is not ClassDeclarationSyntax classDeclaration)
        {
            return false;
        }

        return classDeclaration.AttributeLists
            .SelectMany(al => al.Attributes)
            .Any(attr => attr.Name.ToString() == "Api" || attr.Name.ToString() == "ApiAttribute");
    }

    private static ApiValidationResult? Transform(GeneratorSyntaxContext ctx)
    {
        var classDeclaration = (ClassDeclarationSyntax)ctx.Node;
        var classSymbol = ctx.SemanticModel.GetDeclaredSymbol(classDeclaration);

        if (classSymbol == null || !IsApiController(classSymbol))
        {
            return null;
        }

        var methods = classSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.DeclaredAccessibility == Accessibility.Public &&
                       !m.IsStatic &&
                       m.MethodKind == MethodKind.Ordinary)
            .ToList();

        var validatedMethods = methods
            .Where(m => HasValidationAttribute(m))
            .ToList();

        if (validatedMethods.Count == 0)
        {
            return null;
        }

        var validationCode = GenerateApiValidationCode(classSymbol, validatedMethods);
        var fileName = $"{classSymbol.Name}_ApiValidation.g.cs";
        return new ApiValidationResult(fileName, validationCode);
    }

    private static bool IsApiController(INamedTypeSymbol classSymbol)
    {
        return classSymbol.GetAttributes()
            .Any(attr => attr.AttributeClass?.Name == "ApiAttribute" &&
                attr.AttributeClass.ContainingNamespace.ToDisplayString() == "Atelier.Framework.Attributes");
    }

    private static bool HasValidationAttribute(IMethodSymbol method)
    {
        return method.GetAttributes()
            .Any(attr => attr.AttributeClass?.Name == "ValidatedMethodAttribute");
    }

    private static string GenerateApiValidationCode(INamedTypeSymbol classSymbol, List<IMethodSymbol> methods)
    {
        var className = classSymbol.Name;
        var namespaceName = classSymbol.ContainingNamespace.ToDisplayString();

        var methodBlocks = Sequence.BlankLines(methods.Select(m => (Compositor)BuildValidationMethod(classSymbol, m)));

        return new GA.Validation
        {
            NamespaceName = namespaceName,
            ClassName = className,
            Methods = methodBlocks,
        }.Render();
    }

    private static Compositor BuildValidationMethod(INamedTypeSymbol classSymbol, IMethodSymbol method)
    {
        var methodName = method.Name;
        var returnType = method.ReturnType.ToDisplayString();
        var className = classSymbol.Name;

        var parameters = method.Parameters;

        var parameterList = Sequence.CommaList(parameters.Select(p => (Compositor)new GT.ParameterFragment
        {
            ParamType = p.Type.ToDisplayString(),
            ParamName = p.Name,
            DefaultClause = string.Empty,
        })).Render();

        var validations = Sequence.Lines(parameters.SelectMany(p => BuildParameterValidations(p)));

        var argumentList = Sequence.CommaList(parameters.Select(p => (Compositor)new GT.IdentFragment { Text = p.Name })).Render();

        var returnTypeSymbol = method.ReturnType;
        var isVoid = returnTypeSymbol.SpecialType == SpecialType.System_Void;
        var namedReturn = returnTypeSymbol as INamedTypeSymbol;
        var isBareTask = namedReturn is not null
            && namedReturn.Name == "Task"
            && namedReturn.TypeArguments.Length == 0;
        var isTaskWithResult = namedReturn is not null
            && namedReturn.Name == "Task"
            && namedReturn.TypeArguments.Length > 0;

        string methodReturnType;
        string returnKeyword;
        string asyncKeyword;
        string awaitKeyword;

        if (isVoid)
        {
            methodReturnType = "void";
            returnKeyword = string.Empty;
            asyncKeyword = string.Empty;
            awaitKeyword = string.Empty;
        }
        else if (isBareTask)
        {
            methodReturnType = returnType;
            returnKeyword = string.Empty;
            asyncKeyword = "async ";
            awaitKeyword = "await ";
        }
        else if (isTaskWithResult)
        {
            methodReturnType = returnType;
            returnKeyword = "return ";
            asyncKeyword = "async ";
            awaitKeyword = "await ";
        }
        else
        {
            methodReturnType = returnType;
            returnKeyword = "return ";
            asyncKeyword = string.Empty;
            awaitKeyword = string.Empty;
        }

        var serviceCall = new GA.ServiceCall
        {
            ReturnKeyword = returnKeyword,
            AwaitKeyword = awaitKeyword,
            MethodName = methodName,
            ArgumentList = argumentList,
        };

        return new GA.ValidationMethod
        {
            AsyncKeyword = asyncKeyword,
            MethodReturnType = methodReturnType,
            MethodName = methodName,
            ClassName = className,
            ParameterList = parameterList,
            Validations = validations,
            ServiceCall = serviceCall,
        };
    }

    private static IEnumerable<Compositor> BuildParameterValidations(IParameterSymbol parameter)
    {
        if (!AllowsNull(parameter))
        {
            yield return new V.NullCheck { ParamName = parameter.Name };
        }

        foreach (var attr in parameter.GetAttributes())
        {
            var validator = BuildValidator(parameter.Name, attr);
            if (validator is not null)
            {
                yield return validator;
            }
        }
    }

    private static bool AllowsNull(IParameterSymbol parameter)
    {
        return parameter.GetAttributes()
            .Any(attr => attr.AttributeClass?.Name == "AllowNullAttribute") ||
               parameter.Type.IsReferenceType &&
               parameter.NullableAnnotation == NullableAnnotation.Annotated;
    }

    private static V.ValidatorCall? BuildValidator(string parameterName, AttributeData attribute)
    {
        var attributeName = attribute.AttributeClass?.Name;

        return attributeName switch
        {
            "NotNullOrEmptyAttribute" => new V.NotNullOrEmpty { ParamName = parameterName },
            "NotEmptyAttribute" => new V.NotEmpty { ParamName = parameterName },
            "EmailAttribute" => new V.Email { ParamName = parameterName },
            "GuidAttribute" => new V.Guid { ParamName = parameterName },
            "ValidateContractAttribute" => new V.ValidateContract { ParamName = parameterName },
            "ValidateDataAnnotationsAttribute" => new V.ValidateDataAnnotations { ParamName = parameterName },
            _ => null,
        };
    }
}

internal sealed record ApiValidationResult(string HintName, string Source);
