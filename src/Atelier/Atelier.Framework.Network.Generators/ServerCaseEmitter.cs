using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Atelier.Framework.Network.Generators.Compositors;

namespace Atelier.Framework.Network.Transport.CodeGen;

internal static class ServerCaseEmitter
{
    public static ServerCase Emit(IMethodSymbol method)
    {
        var nonCt = method.Parameters
            .Where(p => p.Type.ToDisplayString() != "System.Threading.CancellationToken")
            .ToList();

        var returnHandling = MakeReturnHandling(method);
        var authorizationGuard = MakeAuthorizationGuard(method);

        if (nonCt.Count > 0)
        {
            return new WithParamCase
            {
                MethodName = method.Name,
                ParamType = nonCt[0].Type.ToDisplayString(),
                ReturnHandling = returnHandling,
                AuthorizationGuard = authorizationGuard,
            };
        }

        return new NoParamCase
        {
            MethodName = method.Name,
            ReturnHandling = returnHandling,
            AuthorizationGuard = authorizationGuard,
        };
    }

    private static AuthorizationGuard MakeAuthorizationGuard(IMethodSymbol method)
    {
        var requirement = ReadRequirement(method);

        if (!requirement.RequiresAuthorization)
        {
            return new NoAuthorization();
        }

        return new RequireAuthorization
        {
            GuardBody = BuildGuardBody(requirement),
        };
    }

    private static AuthorizationRequirement ReadRequirement(IMethodSymbol method)
    {
        var methodAuthorization = GetAuthorizationAttribute(method);
        var typeAuthorization = method.ContainingType is { } containingType
            ? GetAuthorizationAttribute(containingType)
            : null;
        var effectiveAuthorization = methodAuthorization ?? typeAuthorization;

        var contract = method.ContainingType is { } owner
            ? ReadContract(owner)
            : null;

        var roles = ReadStringArray(effectiveAuthorization, "Roles");
        var permissions = ReadStringArray(effectiveAuthorization, "Permissions");

        var requiredClaims = contract?.RequiredClaims ?? System.Array.Empty<string>();
        var requiredScopes = contract?.RequiredScopes ?? System.Array.Empty<string>();

        var contractFailsClosed = contract is { } c
            && c.RequiresAuthentication
            && !c.AllowAnonymous;

        var requiresAuthorization = effectiveAuthorization != null
            || roles.Length > 0
            || permissions.Length > 0
            || requiredClaims.Length > 0
            || requiredScopes.Length > 0
            || contractFailsClosed;

        return new AuthorizationRequirement(
            requiresAuthorization,
            roles,
            permissions,
            requiredClaims,
            requiredScopes);
    }

    private static AttributeData? GetAuthorizationAttribute(ISymbol symbol)
    {
        return symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "RequiresAuthorizationAttribute");
    }

    private static ContractAuthorization? ReadContract(INamedTypeSymbol type)
    {
        var attribute = type.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "ContractAttribute");

        if (attribute is null)
        {
            return null;
        }

        var requiresAuthentication = true;
        var allowAnonymous = false;
        var scopes = System.Array.Empty<string>();
        var claims = System.Array.Empty<string>();

        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == "RequiresAuthentication" && argument.Value.Value is bool requires)
            {
                requiresAuthentication = requires;
            }
            else if (argument.Key == "AllowAnonymous" && argument.Value.Value is bool anonymous)
            {
                allowAnonymous = anonymous;
            }
            else if (argument.Key == "RequiredScopes" && !argument.Value.IsNull)
            {
                scopes = ReadTypedConstantArray(argument.Value);
            }
            else if (argument.Key == "RequiredClaims" && !argument.Value.IsNull)
            {
                claims = ReadTypedConstantArray(argument.Value);
            }
        }

        return new ContractAuthorization(requiresAuthentication, allowAnonymous, scopes, claims);
    }

    private static string[] ReadStringArray(AttributeData? attribute, string name)
    {
        if (attribute is null)
        {
            return System.Array.Empty<string>();
        }

        var argument = attribute.NamedArguments
            .FirstOrDefault(na => na.Key == name)
            .Value;

        if (argument.IsNull)
        {
            return System.Array.Empty<string>();
        }

        return ReadTypedConstantArray(argument);
    }

    private static string[] ReadTypedConstantArray(TypedConstant value)
    {
        if (value.Kind != TypedConstantKind.Array)
        {
            return System.Array.Empty<string>();
        }

        return value.Values
            .Select(v => v.Value?.ToString() ?? string.Empty)
            .Where(s => s.Length > 0)
            .ToArray();
    }

    private static string BuildGuardBody(AuthorizationRequirement requirement)
    {
        var lines = new List<string>
        {
            "if (message.VerifiedAuthorization?.IsValid() != true)",
            "{",
            "    return Outcome.Failure();",
            "}",
        };

        foreach (var role in requirement.Roles)
        {
            var roleLiteral = SymbolDisplay.FormatLiteral(role, quote: true);
            lines.Add($"if (!message.VerifiedAuthorization.HasRole({roleLiteral}))");
            lines.Add("{");
            lines.Add("    return Outcome.Failure();");
            lines.Add("}");
        }

        foreach (var permission in requirement.Permissions)
        {
            var permissionLiteral = SymbolDisplay.FormatLiteral(permission, quote: true);
            lines.Add($"if (!message.VerifiedAuthorization.HasPermission({permissionLiteral}))");
            lines.Add("{");
            lines.Add("    return Outcome.Failure();");
            lines.Add("}");
        }

        foreach (var claim in requirement.RequiredClaims)
        {
            var claimLiteral = SymbolDisplay.FormatLiteral(claim, quote: true);
            lines.Add($"if (!message.VerifiedAuthorization.HasClaim({claimLiteral}))");
            lines.Add("{");
            lines.Add("    return Outcome.Failure();");
            lines.Add("}");
        }

        if (requirement.RequiredScopes.Length > 0)
        {
            lines.Add("var grantedScopes = message.VerifiedAuthorization.GetClaim<string>(\"scope\")?");
            lines.Add("    .Split(' ', global::System.StringSplitOptions.RemoveEmptyEntries)");
            lines.Add("    .ToHashSet() ?? new global::System.Collections.Generic.HashSet<string>();");

            foreach (var scope in requirement.RequiredScopes)
            {
                var scopeLiteral = SymbolDisplay.FormatLiteral(scope, quote: true);
                lines.Add($"if (!grantedScopes.Contains({scopeLiteral}))");
                lines.Add("{");
                lines.Add("    return Outcome.Failure();");
                lines.Add("}");
            }
        }

        return string.Join("\n", lines);
    }

    private static ReturnHandling MakeReturnHandling(IMethodSymbol method)
    {
        if (method.ReturnType is not INamedTypeSymbol named
            || (named.ConstructedFrom.Name != "Task" && named.ConstructedFrom.Name != "ValueTask")
            || named.TypeArguments.Length == 0)
        {
            return new OutcomeReturn();
        }

        if (named.TypeArguments[0] is INamedTypeSymbol arg
            && arg.Name == "Outcome")
        {
            if (arg.TypeArguments.Length == 0)
            {
                return new OutcomeReturn();
            }

            return new GenericReturn();
        }

        return new PlainReturn();
    }

    private readonly record struct ContractAuthorization(
        bool RequiresAuthentication,
        bool AllowAnonymous,
        string[] RequiredScopes,
        string[] RequiredClaims);

    private readonly record struct AuthorizationRequirement(
        bool RequiresAuthorization,
        string[] Roles,
        string[] Permissions,
        string[] RequiredClaims,
        string[] RequiredScopes);
}
