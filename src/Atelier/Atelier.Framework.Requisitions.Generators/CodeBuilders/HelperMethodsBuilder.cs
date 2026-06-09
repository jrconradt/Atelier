using Microsoft.CodeAnalysis;
using Templar.Rendering;
using G = Atelier.Framework.Requisitions.Generators.Templates.Helper;
using S = Atelier.Framework.Requisitions.Generators.Compositors.Helper.HelperSignatures;
using B = Atelier.Framework.Requisitions.Generators.Compositors.Helper.HelperBodies;

namespace Atelier.Framework.Generators.Requisition;

internal class HelperMethodsBuilder
{
    private readonly INamedTypeSymbol _typeSymbol;
    private readonly FactoryTypeInfo _lifecycleInfo;

    public HelperMethodsBuilder(INamedTypeSymbol typeSymbol, FactoryTypeInfo lifecycleInfo)
    {
        _typeSymbol = typeSymbol;
        _lifecycleInfo = lifecycleInfo;
    }

    public Compositor BuildCompositor()
    {
        var typeName = _typeSymbol.Name;
        var isValueObject = _lifecycleInfo.IsPooled &&
            _typeSymbol.GetAttributes().Any(attr => attr.AttributeClass?.Name == "ValueObjectAttribute");

        Compositor signature;
        Compositor body;

        if (isValueObject)
        {
            signature = new S.ValueObjectSignature { TypeName = typeName };
            body = BuildValueObjectBody(typeName);
        }
        else if (_lifecycleInfo.IsPooled)
        {
            signature = new S.StandardPooledSignature { TypeName = typeName };
            body = new B.StandardPooledBody { MemberMappings = BuildMemberMappings() };
        }
        else
        {
            signature = new S.NonPooledSignature { TypeName = typeName };
            body = new B.NonPooledBody
            {
                TypeName = typeName,
                MemberMappings = BuildMemberMappings(),
            };
        }

        return new G.HelperMethod
        {
            Signature = signature,
            Body = body,
        };
    }

    public string Build() => BuildCompositor().Render();

    private Compositor BuildValueObjectBody(string typeName)
    {
        var constructors = _typeSymbol.GetMembers().OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Constructor &&
                       m.DeclaredAccessibility == Accessibility.Public &&
                       !m.IsStatic).ToList();

        var properties = _typeSymbol.GetMembers().OfType<IPropertySymbol>()
            .Where(p => p.DeclaredAccessibility == Accessibility.Public && !p.IsStatic).ToList();

        var fields = _typeSymbol.GetMembers().OfType<IFieldSymbol>()
            .Where(f => f.DeclaredAccessibility == Accessibility.Public && !f.IsStatic).ToList();

        var bestConstructor = FindBestConstructor(constructors, properties, fields);

        if (bestConstructor == null)
        {
            return new B.ValueObjectNoCtorBody { TypeName = typeName };
        }

        var paramExtractions = new List<Compositor>();
        var paramValues = new List<Compositor>();

        foreach (var param in bestConstructor.Parameters)
        {
            var camelName = GeneratorNaming.ToCamelCase(param.Name);
            paramExtractions.Add(new G.ValueObjectParamBlock
            {
                CamelName = camelName,
                ParamName = param.Name,
            });
            paramValues.Add(new G.ParamValueRef { CamelName = camelName });
        }

        return new B.ValueObjectBody
        {
            TypeName = typeName,
            ParamExtractions = Sequence.Lines(paramExtractions),
            ParamValues = Sequence.CommaList(paramValues),
        };
    }

    private Sequence BuildMemberMappings()
    {
        var blocks = new List<Compositor>();

        var properties = _typeSymbol.GetMembers().OfType<IPropertySymbol>()
            .Where(p => p.DeclaredAccessibility == Accessibility.Public &&
                       !p.IsStatic &&
                       p.SetMethod != null);

        foreach (var property in properties)
        {
            blocks.Add(new G.PropertyMapBlock
            {
                CamelName = GeneratorNaming.ToCamelCase(property.Name),
                MemberName = property.Name,
                MemberType = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            });
        }

        var fields = _typeSymbol.GetMembers().OfType<IFieldSymbol>()
            .Where(f => f.DeclaredAccessibility == Accessibility.Public &&
                       !f.IsStatic &&
                       !f.IsReadOnly);

        foreach (var field in fields)
        {
            blocks.Add(new G.FieldMapBlock
            {
                CamelName = GeneratorNaming.ToCamelCase(field.Name),
                FieldName = field.Name,
                FieldType = field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            });
        }

        return Sequence.BlankLines(blocks);
    }

    private static IMethodSymbol? FindBestConstructor(
        List<IMethodSymbol> constructors,
        List<IPropertySymbol> properties,
        List<IFieldSymbol> fields)
    {
        var parameterless = constructors.FirstOrDefault(c => c.Parameters.Length == 0);
        if (parameterless != null)
        {
            return parameterless;
        }

        IMethodSymbol? bestMatch = null;
        var maxMatchCount = 0;

        foreach (var constructor in constructors.Where(c => c.Parameters.Length > 0))
        {
            var matchCount = 0;
            var paramNames = constructor.Parameters.Select(p => p.Name.ToLowerInvariant()).ToHashSet();

            foreach (var property in properties)
            {
                if (paramNames.Contains(property.Name.ToLowerInvariant()))
                {
                    matchCount++;
                }
            }

            foreach (var field in fields)
            {
                if (paramNames.Contains(field.Name.ToLowerInvariant()))
                {
                    matchCount++;
                }
            }

            if (matchCount > maxMatchCount)
            {
                maxMatchCount = matchCount;
                bestMatch = constructor;
            }
        }

        return bestMatch;
    }
}
