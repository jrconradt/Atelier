using Templar.Rendering;
using Templar.Presets;
using Microsoft.CodeAnalysis;
using E = Atelier.Framework.Infrastructure.Generators.Templates.Endpoint;

namespace Atelier.Framework.Infrastructure.Generators;

internal class EndpointCodeBuilder
{
    private readonly INamedTypeSymbol _classSymbol;
    private readonly List<IMethodSymbol> _apiMethods;
    private readonly string[] _claims;

    public EndpointCodeBuilder(INamedTypeSymbol classSymbol, List<IMethodSymbol> apiMethods, string[] claims)
    {
        _classSymbol = classSymbol;
        _apiMethods = apiMethods;
        _claims = claims;
    }

    public string Build()
    {
        var namespaceName = _classSymbol.ContainingNamespace.ToDisplayString();
        var className = _classSymbol.Name;

        var mappings = Sequence.Lines(_apiMethods.Select(m =>
            (Compositor)new SingleEndpointGenerator(_classSymbol, m, _claims).BuildCompositor()));

        var endpointsClass = new E.EndpointsClass
        {
            ClassName = className,
            Mappings = mappings,
        };

        return new EndpointFile
        {
            Namespace = namespaceName,
            Usings = new[]
            {
                "Microsoft.AspNetCore.Builder",
                "Microsoft.AspNetCore.Http",
                "Microsoft.AspNetCore.Routing",
                "Microsoft.Extensions.DependencyInjection"
            },
            Body = endpointsClass.Render(),
        }.Render();
    }

    private sealed class EndpointFile : CSharpFile { }
}
