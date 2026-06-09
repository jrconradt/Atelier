using Templar.Rendering;
using Microsoft.CodeAnalysis;
using GT = Atelier.Framework.Infrastructure.Generators.Templates;

namespace Atelier.Framework.Infrastructure.Generators;

internal class ParameterBuilder
{
    private static readonly SymbolDisplayFormat FullyQualifiedFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    private readonly IMethodSymbol _method;

    public ParameterBuilder(IMethodSymbol method)
    {
        _method = method;
    }

    public EndpointParameterFragments Build()
    {
        var routeParamItems = new List<Compositor>();
        var serviceCallItems = new List<Compositor>();

        foreach (var param in _method.Parameters)
        {
            var paramName = param.Name;
            var paramType = param.Type.ToDisplayString(FullyQualifiedFormat);

            routeParamItems.Add(new GT.ParameterFragment
            {
                ParamType = paramType,
                ParamName = paramName,
                DefaultClause = string.Empty,
            });
            serviceCallItems.Add(new GT.IdentFragment { Text = paramName });
        }

        var routeParamRendered = Sequence.CommaList(routeParamItems).Render();
        var serviceCallRendered = Sequence.CommaList(serviceCallItems).Render();

        var routeParamString = routeParamItems.Count > 0 ? ", " + routeParamRendered : string.Empty;
        var serviceCallParamString = serviceCallRendered;

        return new EndpointParameterFragments
        {
            RouteParameterList = routeParamString,
            ServiceCallArgumentList = serviceCallParamString
        };
    }
}
