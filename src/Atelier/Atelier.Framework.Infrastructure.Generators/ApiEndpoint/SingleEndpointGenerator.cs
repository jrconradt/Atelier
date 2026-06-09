using Templar.Rendering;
using Microsoft.CodeAnalysis;
using E = Atelier.Framework.Infrastructure.Generators.Templates.Endpoint;

namespace Atelier.Framework.Infrastructure.Generators;

internal class SingleEndpointGenerator
{
    private static readonly SymbolDisplayFormat FullyQualifiedFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    private readonly INamedTypeSymbol _classSymbol;
    private readonly IMethodSymbol _method;
    private readonly string[] _claims;

    public SingleEndpointGenerator(INamedTypeSymbol classSymbol, IMethodSymbol method, string[] claims)
    {
        _classSymbol = classSymbol;
        _method = method;
        _claims = claims;
    }

    public string Generate() => BuildCompositor().Render();

    public Compositor BuildCompositor()
    {
        var endpointDetails = GetEndpointDetails();
        var parameterFragments = GetParameterFragments();
        var responseHandler = new ResponseHandlerGenerator(_method);

        var serviceType = _classSymbol.ToDisplayString(FullyQualifiedFormat);
        var serviceCall = BuildServiceCall(parameterFragments, serviceType);
        var responseCode = responseHandler.BuildResponseHandler();
        var authGuard = BuildInlineAuthGuard();

        return new E.Map
        {
            HttpMethod = endpointDetails.HttpMethod,
            FullRoute = endpointDetails.FullRoute,
            RouteParamString = parameterFragments.RouteParameterList,
            ServiceType = serviceType,
            ServiceCall = serviceCall,
            ResponseCode = responseCode,
            AuthGuard = authGuard,
            OperationMethod = _method.Name,
        };
    }

    private const string DefaultClaimType = "permission";

    private Compositor? BuildInlineAuthGuard()
    {
        if (AllowsAnonymous())
        {
            return null;
        }

        var distinctClaims = _claims
            .Distinct()
            .ToArray();

        var claimChecks = string.Join("\n            || ",
                                      distinctClaims.Select(c => new E.AuthClaimCheck
                                      {
                                          ClaimType = DefaultClaimType,
                                          ClaimValue = c.Replace("\"", "\\\""),
                                      }.Render().TrimEnd()));

        var requiredClaimsList = string.Join(", ", distinctClaims)
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");

        return new E.AuthGuard
        {
            ClaimChecks = claimChecks,
            RequiredClaimsList = requiredClaimsList,
        };
    }

    private bool AllowsAnonymous()
    {
        return HasAllowAnonymous(_method)
            || HasAllowAnonymous(_classSymbol);
    }

    private static bool HasAllowAnonymous(ISymbol symbol)
    {
        return symbol.GetAttributes()
            .Any(a => a.AttributeClass?.Name == "AllowAnonymousAttribute");
    }

    private EndpointDetails GetEndpointDetails()
    {
        var resourceName = NamingConventions.ExtractResourceName(_classSymbol.Name);
        var baseRoute = NamingConventions.ToPluralKebabCase(resourceName);

        var (httpMethod, routePattern, hasIdParameter) = NamingConventions.InferEndpointDetails(_method.Name, resourceName);

        return new EndpointDetails
        {
            HttpMethod = httpMethod,
            RoutePattern = routePattern,
            FullRoute = "api/" + baseRoute + routePattern,
            HasIdParameter = hasIdParameter,
        };
    }

    private EndpointParameterFragments GetParameterFragments()
    {
        var paramBuilder = new ParameterBuilder(_method);
        return paramBuilder.Build();
    }

    private Compositor BuildServiceCall(EndpointParameterFragments parameterFragments, string serviceType)
    {
        var hasValidation = _method.GetAttributes()
            .Any(attr => attr.AttributeClass?.Name == "ValidatedMethodAttribute");

        if (hasValidation)
        {
            return new E.ValidatedServiceCall
            {
                ServiceType = serviceType,
                MethodName = _method.Name,
                ArgumentList = parameterFragments.ServiceCallArgumentList,
            };
        }

        return new E.PlainServiceCall
        {
            MethodName = _method.Name,
            ArgumentList = parameterFragments.ServiceCallArgumentList,
        };
    }
}

internal class EndpointDetails
{
    public string HttpMethod { get; set; } = string.Empty;
    public string RoutePattern { get; set; } = string.Empty;
    public string FullRoute { get; set; } = string.Empty;
    public bool HasIdParameter { get; set; }
}

internal class EndpointParameterFragments
{
    public string RouteParameterList { get; set; } = string.Empty;
    public string ServiceCallArgumentList { get; set; } = string.Empty;
}
