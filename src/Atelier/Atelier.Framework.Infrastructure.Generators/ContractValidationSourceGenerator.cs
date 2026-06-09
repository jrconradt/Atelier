using System.Collections.Immutable;
using Templar.Rendering;
using Templar.Presets;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using C = Atelier.Framework.Infrastructure.Generators.Templates.Contract;
using V = Atelier.Framework.Infrastructure.Generators.Compositors.Contract.Validations;

namespace Atelier.Framework.Compiler.Generators.Contract;

[Generator]
public sealed class ContractValidationSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var contracts = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => IsCandidate(node),
                static (ctx, _) => Transform(ctx))
            .Where(static result => result is not null)
            .Select(static (result, _) => result!)
            .Collect();

        context.RegisterSourceOutput(
            contracts,
            static (spc, results) => Emit(spc, results));
    }

    private static bool IsCandidate(SyntaxNode node)
    {
        if (node is not ClassDeclarationSyntax classDeclaration)
        {
            return false;
        }

        return classDeclaration.AttributeLists
            .SelectMany(al => al.Attributes)
            .Any(attr => attr.Name.ToString() is "Contract" or "ContractAttribute");
    }

    private static ContractValidationResult? Transform(GeneratorSyntaxContext ctx)
    {
        var classDeclaration = (ClassDeclarationSyntax)ctx.Node;
        var classSymbol = ctx.SemanticModel.GetDeclaredSymbol(classDeclaration);

        if (classSymbol == null || !HasContractAttribute(classSymbol))
        {
            return null;
        }

        if (classSymbol.ContainingType is not null)
        {
            return null;
        }
        if (classSymbol.DeclaredAccessibility != Accessibility.Public)
        {
            return null;
        }

        var properties = classSymbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => p.DeclaredAccessibility == Accessibility.Public && !p.IsStatic)
            .ToList();

        if (properties.Count == 0)
        {
            return null;
        }

        var validationCode = GenerateContractValidationCode(classSymbol, properties);
        var baseName = classSymbol.Name;
        var namespacePart = classSymbol.ContainingNamespace.ToDisplayString().Replace(".", "_");
        return new ContractValidationResult(baseName, namespacePart, validationCode);
    }

    private static void Emit(SourceProductionContext context, ImmutableArray<ContractValidationResult> results)
    {
        foreach (var result in results)
        {
            var fileName = $"{result.NamespacePart}_{result.BaseName}_ContractValidation.g.cs";

            context.AddSource(
                fileName,
                SourceText.From(
                    result.ValidationCode,
                    System.Text.Encoding.UTF8));
        }
    }

    private static bool HasContractAttribute(INamedTypeSymbol classSymbol)
    {
        return classSymbol.GetAttributes()
            .Any(attr => attr.AttributeClass?.Name == "ContractAttribute");
    }

    private static string GenerateContractValidationCode(
        INamedTypeSymbol classSymbol,
        List<IPropertySymbol> properties)
    {
        var className = classSymbol.Name;
        var namespaceName = classSymbol.ContainingNamespace.ToDisplayString();

        var typeParamNames = string.Join(", ", classSymbol.TypeParameters.Select(tp => tp.Name));
        var typeParams = classSymbol.IsGenericType ? $"<{typeParamNames}>" : string.Empty;
        var targetType = $"{className}{typeParams}";
        var constraints = BuildConstraintClauses(classSymbol);

        var aliasLines = Sequence.Lines(properties.Select(p => (Compositor)new V.AliasLine { PropertyName = p.Name }));

        var perPropertyGroups = properties
            .Select(p => BuildPropertyValidationGroup(p))
            .Where(g => g is not null)
            .Select(g => g!)
            .ToList();

        var propertyValidationGroups = Sequence.BlankLines(perPropertyGroups);

        var combinedValidations = Sequence.BlankLines(new IComposable[] { aliasLines, propertyValidationGroups });

        var extensions = new C.Extensions
        {
            ClassName = className,
            TargetType = targetType,
            TypeParams = typeParams,
            Constraints = constraints,
            PropertyValidations = combinedValidations,
        };

        return new ContractValidationFile
        {
            Namespace = namespaceName,
            Body = extensions.Render(),
        }.Render();
    }

    private sealed class ContractValidationFile : CSharpFile { }

    private static readonly SymbolDisplayFormat ConstraintTypeFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static string BuildConstraintClauses(INamedTypeSymbol classSymbol)
    {
        if (!classSymbol.IsGenericType)
        {
            return string.Empty;
        }

        var clauses = new List<string>();

        foreach (var tp in classSymbol.TypeParameters)
        {
            var parts = new List<string>();

            if (tp.HasReferenceTypeConstraint)
            {
                parts.Add("class");
            }

            if (tp.HasUnmanagedTypeConstraint)
            {
                parts.Add("unmanaged");
            }
            else if (tp.HasValueTypeConstraint)
            {
                parts.Add("struct");
            }

            if (tp.HasNotNullConstraint)
            {
                parts.Add("notnull");
            }

            foreach (var constraintType in tp.ConstraintTypes)
            {
                parts.Add(constraintType.ToDisplayString(ConstraintTypeFormat));
            }

            if (tp.HasConstructorConstraint)
            {
                parts.Add("new()");
            }

            if (parts.Count == 0)
            {
                continue;
            }

            clauses.Add($"where {tp.Name} : {string.Join(", ", parts)}");
        }

        if (clauses.Count == 0)
        {
            return string.Empty;
        }

        return $" {string.Join(" ", clauses)}";
    }

    private static Sequence? BuildPropertyValidationGroup(IPropertySymbol property)
    {
        var propertyName = property.Name;
        var propertyType = property.Type;
        var attributes = property.GetAttributes();

        var validations = new List<Compositor>();

        if (propertyType.TypeKind == TypeKind.Enum)
        {
            validations.Add(new V.EnumValidation
            {
                PropertyName = propertyName,
                EnumTypeName = propertyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            });
        }

        foreach (var attr in attributes)
        {
            var validation = BuildAttributeValidation(propertyName, propertyType, attr);
            if (validation is not null)
            {
                validations.Add(validation);
            }
        }

        var hasRequiredAttribute = attributes.Any(attr =>
            attr.AttributeClass?.Name == "RequiredAttribute");

        if (!hasRequiredAttribute &&
            !IsNullableReferenceType(property) &&
            propertyType.IsReferenceType)
        {
            validations.Insert(0, new V.NullCheck { PropertyName = propertyName });
        }

        if (validations.Count == 0)
        {
            return null;
        }

        return Sequence.BlankLines(validations);
    }

    private static bool IsNullableReferenceType(IPropertySymbol property)
    {
        return property.NullableAnnotation == NullableAnnotation.Annotated;
    }

    private static Compositor? BuildAttributeValidation(
        string propertyName,
        ITypeSymbol propertyType,
        AttributeData attribute)
    {
        var attributeName = attribute.AttributeClass?.Name;

        return attributeName switch
        {
            "RequiredAttribute" => BuildRequiredValidation(propertyName, propertyType),
            "StringLengthAttribute" => BuildStringLengthValidation(propertyName, attribute),
            "MinLengthAttribute" => BuildMinLengthValidation(propertyName, propertyType, attribute),
            "MaxLengthAttribute" => BuildMaxLengthValidation(propertyName, propertyType, attribute),
            "RangeAttribute" => BuildRangeValidation(propertyName, attribute),
            "RegularExpressionAttribute" => BuildRegexValidation(propertyName, attribute),
            "EmailAddressAttribute" => new V.EmailValidation { PropertyName = propertyName },
            "UrlAttribute" => new V.UrlValidation { PropertyName = propertyName },
            "PhoneAttribute" => new V.PhoneValidation { PropertyName = propertyName },
            "CreditCardAttribute" => new V.CreditCardValidation { PropertyName = propertyName },
            _ => null,
        };
    }

    private static Compositor BuildRequiredValidation(string propertyName, ITypeSymbol propertyType)
    {
        if (propertyType.SpecialType == SpecialType.System_String)
        {
            return new V.RequiredString { PropertyName = propertyName };
        }
        return new V.RequiredObject { PropertyName = propertyName };
    }

    private static Compositor BuildStringLengthValidation(string propertyName, AttributeData attribute)
    {
        var maxLength = attribute.ConstructorArguments.FirstOrDefault().Value;
        var minLength = attribute.NamedArguments
            .FirstOrDefault(na => na.Key == "MinimumLength")
            .Value.Value;

        if (minLength != null)
        {
            return new V.StringLengthRange
            {
                PropertyName = propertyName,
                MinLength = minLength.ToString() ?? "0",
                MaxLength = maxLength?.ToString() ?? "0",
            };
        }
        return new V.StringLengthMax
        {
            PropertyName = propertyName,
            MaxLength = maxLength?.ToString() ?? "0",
        };
    }

    private static Compositor BuildMinLengthValidation(string propertyName, ITypeSymbol propertyType, AttributeData attribute)
    {
        var minLength = attribute.ConstructorArguments.FirstOrDefault().Value;
        return new V.MinLengthValidation
        {
            PropertyName = propertyName,
            LengthProperty = GetLengthPropertyName(propertyType),
            MinLength = minLength?.ToString() ?? "0",
        };
    }

    private static Compositor BuildMaxLengthValidation(string propertyName, ITypeSymbol propertyType, AttributeData attribute)
    {
        var maxLength = attribute.ConstructorArguments.FirstOrDefault().Value;
        return new V.MaxLengthValidation
        {
            PropertyName = propertyName,
            LengthProperty = GetLengthPropertyName(propertyType),
            MaxLength = maxLength?.ToString() ?? "0",
        };
    }

    private static string GetLengthPropertyName(ITypeSymbol propertyType)
    {
        if (propertyType.SpecialType == SpecialType.System_String)
        {
            return "Length";
        }

        if (propertyType is IArrayTypeSymbol)
        {
            return "Length";
        }

        return "Count";
    }

    private static Compositor BuildRangeValidation(string propertyName, AttributeData attribute)
    {
        var min = attribute.ConstructorArguments.ElementAtOrDefault(0).Value;
        var max = attribute.ConstructorArguments.ElementAtOrDefault(1).Value;

        return new V.RangeValidation
        {
            PropertyName = propertyName,
            Min = FormatRangeBound(min),
            Max = FormatRangeBound(max),
        };
    }

    private static string FormatRangeBound(object? bound)
    {
        if (bound is null)
        {
            return "0";
        }

        return bound switch
        {
            double d => $"{SymbolDisplay.FormatPrimitive(d, quoteStrings: false, useHexadecimalNumbers: false)}D",
            float f => $"{SymbolDisplay.FormatPrimitive(f, quoteStrings: false, useHexadecimalNumbers: false)}F",
            decimal m => $"{SymbolDisplay.FormatPrimitive(m, quoteStrings: false, useHexadecimalNumbers: false)}M",
            long l => $"{SymbolDisplay.FormatPrimitive(l, quoteStrings: false, useHexadecimalNumbers: false)}L",
            _ => SymbolDisplay.FormatPrimitive(bound, quoteStrings: false, useHexadecimalNumbers: false),
        };
    }

    private static Compositor? BuildRegexValidation(string propertyName, AttributeData attribute)
    {
        var pattern = attribute.ConstructorArguments.FirstOrDefault().Value?.ToString();
        if (string.IsNullOrEmpty(pattern))
        {
            return null;
        }

        return new V.RegexValidation
        {
            PropertyName = propertyName,
            Pattern = SymbolDisplay.FormatLiteral(pattern!, quote: true),
        };
    }
}

internal sealed record ContractValidationResult(
    string BaseName,
    string NamespacePart,
    string ValidationCode);
