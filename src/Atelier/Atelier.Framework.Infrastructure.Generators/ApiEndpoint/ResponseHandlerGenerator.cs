using Microsoft.CodeAnalysis;
using Templar.Rendering;
using R = Atelier.Framework.Infrastructure.Generators.Compositors.ResponseHandlers;

namespace Atelier.Framework.Infrastructure.Generators;

internal class ResponseHandlerGenerator
{
    private readonly IMethodSymbol _method;

    public ResponseHandlerGenerator(IMethodSymbol method)
    {
        _method = method;
    }

    public string GenerateResponseHandling() => BuildResponseHandler().Render();

    public Compositor BuildResponseHandler()
    {
        var innerType = UnwrapAsync(_method.ReturnType);

        if (IsOutcome(innerType))
        {
            return new R.OutcomeBareResponse();
        }

        if (IsGenericOutcome(innerType, out var dataType))
        {
            if (IsEnumerable(dataType))
            {
                return new R.OutcomeEnumerableResponse();
            }
            return new R.OutcomeSingleResponse();
        }

        return new R.PlainResponse();
    }

    private static ITypeSymbol UnwrapAsync(ITypeSymbol returnType)
    {
        if (returnType is INamedTypeSymbol named && named.IsGenericType)
        {
            var def = named.ConstructedFrom.ToDisplayString();
            if (def == "System.Threading.Tasks.Task<TResult>" ||
                def == "System.Threading.Tasks.ValueTask<TResult>")
            {
                return named.TypeArguments[0];
            }
        }
        return returnType;
    }

    private static bool IsOutcome(ITypeSymbol type)
    {
        return type.Name == "Outcome" &&
               type is INamedTypeSymbol named && !named.IsGenericType;
    }

    private static bool IsGenericOutcome(ITypeSymbol type, out ITypeSymbol? dataType)
    {
        if (type is INamedTypeSymbol named &&
            named.IsGenericType &&
            named.Name == "Outcome" &&
            named.TypeArguments.Length == 1)
        {
            dataType = named.TypeArguments[0];
            return true;
        }
        dataType = null;
        return false;
    }

    private static bool IsEnumerable(ITypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        if (type.SpecialType == SpecialType.System_String)
        {
            return false;
        }

        if (type is IArrayTypeSymbol)
        {
            return true;
        }

        if (type is INamedTypeSymbol named)
        {
            foreach (var iface in named.AllInterfaces)
            {
                if (iface.Name == "IEnumerable"
                    && (iface.ContainingNamespace.ToDisplayString() == "System.Collections"
                     || iface.ContainingNamespace.ToDisplayString() == "System.Collections.Generic"))
                {
                    return true;
                }
            }

            if (named.Name == "IEnumerable" || named.Name == "List"
                || named.Name == "IReadOnlyList" || named.Name == "ICollection"
                || named.Name == "HashSet" || named.Name == "IReadOnlyCollection")
            {
                return true;
            }
        }
        return false;
    }
}
