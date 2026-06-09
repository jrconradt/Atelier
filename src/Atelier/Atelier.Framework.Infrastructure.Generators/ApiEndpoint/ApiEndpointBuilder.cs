using System.Text;
using Microsoft.CodeAnalysis;

namespace Atelier.Framework.Infrastructure.Generators;

internal class ApiEndpointBuilder
{
    private readonly INamedTypeSymbol _classSymbol;

    public ApiEndpointBuilder(INamedTypeSymbol classSymbol)
    {
        _classSymbol = classSymbol;
    }

    public string Build()
    {
        var apiAttr = GetApiAttribute();
        if (apiAttr == null)
        {
            return string.Empty;
        }

        var apiMethods = GetApiMethods();
        if (apiMethods.Count == 0)
        {
            return string.Empty;
        }

        var claims = ExtractClaims(apiAttr);
        var codeBuilder = new EndpointCodeBuilder(_classSymbol, apiMethods, claims);
        return codeBuilder.Build();
    }

    private AttributeData? GetApiAttribute()
    {
        return _classSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "ApiAttribute" &&
                               a.AttributeClass.ContainingNamespace.ToDisplayString() == "Atelier.Framework.Attributes");
    }

    private static string[] ExtractClaims(AttributeData apiAttr)
    {
        if (apiAttr.ConstructorArguments.Length == 0)
        {
            return System.Array.Empty<string>();
        }
        var arg = apiAttr.ConstructorArguments[0];
        if (arg.IsNull)
        {
            return System.Array.Empty<string>();
        }
        if (arg.Kind != TypedConstantKind.Array)
        {
            return System.Array.Empty<string>();
        }
        return arg.Values
            .Select(v => v.Value?.ToString() ?? string.Empty)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray();
    }

    private List<IMethodSymbol> GetApiMethods()
    {
        var isMvcController = false;

        return _classSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => IsValidApiMethod(m, isMvcController))
            .ToList();
    }

    private static bool IsValidApiMethod(IMethodSymbol method, bool isMvcController)
    {
        if (method.DeclaredAccessibility != Accessibility.Public)
        {
            return false;
        }
        if (method.MethodKind != MethodKind.Ordinary)
        {
            return false;
        }
        if (method.IsStatic)
        {
            return false;
        }
        if (method.ReturnsVoid)
        {
            return false;
        }
        if (!method.ReturnType.Name.Contains("Task", System.StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (method.IsGenericMethod)
        {
            return false;
        }
        if (method.TypeParameters.Length > 0)
        {
            return false;
        }

        if (method.ReturnType is not INamedTypeSymbol namedReturnType)
        {
            return false;
        }

        return namedReturnType.TypeArguments.Length > 0 ||
               (isMvcController && namedReturnType.Name == "Task");
    }
}
