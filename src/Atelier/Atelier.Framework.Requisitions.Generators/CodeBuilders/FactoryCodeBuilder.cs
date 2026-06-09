using Microsoft.CodeAnalysis;
using Templar.Rendering;
using Templar.Presets;
using G = Atelier.Framework.Requisitions.Generators.Templates.Factory;
using F = Atelier.Framework.Requisitions.Generators.Compositors.Factory.FactoryFields;
using C = Atelier.Framework.Requisitions.Generators.Compositors.Factory.FactoryConstructors;
using R = Atelier.Framework.Requisitions.Generators.Compositors.Factory.ReturnAndResetMethods;

namespace Atelier.Framework.Generators.Requisition;

internal class FactoryCodeBuilder
{
    private readonly INamedTypeSymbol _typeSymbol;
    private readonly FactoryTypeInfo _lifecycleInfo;

    public FactoryCodeBuilder(INamedTypeSymbol typeSymbol, FactoryTypeInfo lifecycleInfo)
    {
        _typeSymbol = typeSymbol;
        _lifecycleInfo = lifecycleInfo;
    }

    public string Build()
    {
        var typeName = _typeSymbol.Name;
        var namespaceName = _typeSymbol.ContainingNamespace.ToDisplayString();
        var hasContractAttribute = HasContractAttribute(_typeSymbol);

        var body = new G.FactoryFile
        {
            TypeName = typeName,
            Fields = Sequence.BlankLines(BuildFields(typeName, hasContractAttribute)),
            Constructor = BuildConstructor(typeName, hasContractAttribute),
            CreateMethod = new CreateMethodBuilder(_typeSymbol, _lifecycleInfo).BuildCompositor(),
            HelperMethods = new HelperMethodsBuilder(_typeSymbol, _lifecycleInfo).BuildCompositor(),
            ReturnAndResetMethods = BuildReturnAndResetMethods(typeName),
        };

        return new CSharpFile
        {
            Namespace = namespaceName,
            Usings = BuildUsings(hasContractAttribute),
            Body = body.Render(),
        }.Render();
    }

    private static IEnumerable<string> BuildUsings(bool hasContractAttribute)
    {
        yield return "System";
        yield return "Atelier.Framework.Requisitions";
        if (hasContractAttribute)
        {
            yield return "Atelier.Framework.Attributes";
        }
    }

    private IEnumerable<Compositor> BuildFields(string typeName, bool hasContractAttribute)
    {
        if (_lifecycleInfo.IsPooled)
        {
            yield return new F.PoolField { TypeName = typeName };
        }
        if (hasContractAttribute)
        {
            yield return new F.ValidatorField();
        }
    }

    private Compositor BuildConstructor(string typeName, bool hasContractAttribute)
    {
        var assignments = new List<Compositor>();

        if (hasContractAttribute)
        {
            assignments.Add(new G.ValidatorAssign());
        }

        if (_lifecycleInfo.IsPooled)
        {
            assignments.Add(new G.PoolAssign
            {
                TypeName = typeName,
                MaxSize = _lifecycleInfo.MaxPoolSize,
                InitialSize = _lifecycleInfo.InitialPoolSize,
            });
        }

        var assignmentLines = Sequence.Lines(assignments);

        return hasContractAttribute
            ? new C.ValidatorCtor { TypeName = typeName, Assignments = assignmentLines }
            : new C.ParameterlessCtor { TypeName = typeName, Assignments = assignmentLines };
    }

    private Compositor BuildReturnAndResetMethods(string typeName) =>
        _lifecycleInfo.IsPooled
            ? new R.PooledReturnAndReset { TypeName = typeName }
            : new R.UnpooledReturnAndReset { TypeName = typeName };

    private static bool HasContractAttribute(INamedTypeSymbol typeSymbol)
    {
        return typeSymbol.GetAttributes().Any(attr => attr.AttributeClass?.Name == "ContractAttribute");
    }
}
