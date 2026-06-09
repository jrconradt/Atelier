using Microsoft.CodeAnalysis;
using Templar.Rendering;
using Templar.Presets;
using G = Atelier.Framework.Requisitions.Generators.Templates.Pool;
using R = Atelier.Framework.Requisitions.Generators.Compositors.Pool.ResetLines;

namespace Atelier.Framework.Generators.Requisition;

internal class PoolCodeBuilder
{
    private readonly INamedTypeSymbol _typeSymbol;
    private readonly FactoryTypeInfo _lifecycleInfo;

    public PoolCodeBuilder(INamedTypeSymbol typeSymbol, FactoryTypeInfo lifecycleInfo)
    {
        _typeSymbol = typeSymbol;
        _lifecycleInfo = lifecycleInfo;
    }

    public string Build()
    {
        var typeName = _typeSymbol.Name;
        var namespaceName = _typeSymbol.ContainingNamespace.ToDisplayString();

        var body = new G.PoolFile
        {
            TypeName = typeName,
            NamespaceName = namespaceName,
            ResetLines = Sequence.Lines(BuildResetLines()),
        };

        return new CSharpFile
        {
            Namespace = namespaceName,
            Usings = new[]
            {
                "System",
                "System.Collections.Concurrent",
                "System.Threading",
            },
            Body = body.Render(),
        }.Render();
    }

    private IEnumerable<Compositor> BuildResetLines()
    {
        var properties = _typeSymbol.GetMembers().OfType<IPropertySymbol>()
            .Where(p => p.DeclaredAccessibility == Accessibility.Public &&
                       !p.IsStatic &&
                       p.SetMethod != null);

        foreach (var property in properties)
        {
            yield return ResetLineFor("instance", property.Name, property.Type);
        }

        var fields = _typeSymbol.GetMembers().OfType<IFieldSymbol>()
            .Where(f => f.DeclaredAccessibility == Accessibility.Public &&
                       !f.IsStatic &&
                       !f.IsReadOnly);

        foreach (var field in fields)
        {
            yield return ResetLineFor("instance", field.Name, field.Type);
        }
    }

    private static Compositor ResetLineFor(string target, string memberName, ITypeSymbol type)
    {
        if (IsCollectionType(type))
        {
            return new R.CollectionClearLine { Target = target, MemberName = memberName };
        }
        if (type.IsReferenceType)
        {
            return new R.ReferenceDefaultLine { Target = target, MemberName = memberName };
        }
        return new R.ValueDefaultLine { Target = target, MemberName = memberName };
    }

    private static bool IsCollectionType(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        var typeString = namedType.ToDisplayString();
        if (typeString.StartsWith("System.Collections", StringComparison.Ordinal))
        {
            return true;
        }

        var interfaces = namedType.AllInterfaces;
        foreach (var iface in interfaces)
        {
            var ifaceName = iface.ToDisplayString();
            if (ifaceName.StartsWith("System.Collections.Generic.ICollection", StringComparison.Ordinal) ||
                ifaceName.StartsWith("System.Collections.Generic.IList", StringComparison.Ordinal) ||
                ifaceName.StartsWith("System.Collections.Generic.IDictionary", StringComparison.Ordinal) ||
                ifaceName.StartsWith("System.Collections.Generic.ISet", StringComparison.Ordinal) ||
                ifaceName == "System.Collections.ICollection" ||
                ifaceName == "System.Collections.IList")
            {
                return true;
            }
        }

        return false;
    }
}
