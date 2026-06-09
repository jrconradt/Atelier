using Microsoft.CodeAnalysis;
using Templar.Rendering;
using G = Atelier.Framework.Requisitions.Generators.Templates.Create;
using B = Atelier.Framework.Requisitions.Generators.Compositors.Create.CreateBodies;
using V = Atelier.Framework.Requisitions.Generators.Compositors.Create.Validations;

namespace Atelier.Framework.Generators.Requisition;

internal class CreateMethodBuilder
{
    private readonly INamedTypeSymbol _typeSymbol;
    private readonly FactoryTypeInfo _lifecycleInfo;

    public CreateMethodBuilder(INamedTypeSymbol typeSymbol, FactoryTypeInfo lifecycleInfo)
    {
        _typeSymbol = typeSymbol;
        _lifecycleInfo = lifecycleInfo;
    }

    public Compositor BuildCompositor()
    {
        var typeName = _typeSymbol.Name;
        return new G.CreateMethod
        {
            TypeName = typeName,
            Body = BuildBody(typeName),
            Validation = BuildValidation(),
        };
    }

    public string Build() => BuildCompositor().Render();

    private Compositor BuildBody(string typeName)
    {
        var isValueObject = _lifecycleInfo.IsPooled &&
            _typeSymbol.GetAttributes().Any(attr => attr.AttributeClass?.Name == "ValueObjectAttribute");

        if (_lifecycleInfo.IsPooled)
        {
            if (isValueObject)
            {
                return new B.ValueObjectPooledBody { TypeName = typeName };
            }
            return new B.StandardPooledBody { TypeName = typeName };
        }

        return new B.NonPooledBody { TypeName = typeName };
    }

    private Compositor BuildValidation()
    {
        var hasContractAttribute = _typeSymbol.GetAttributes()
            .Any(attr => attr.AttributeClass?.Name == "ContractAttribute");

        return hasContractAttribute
            ? new V.ContractValidation()
            : new V.PlainReturn();
    }
}
