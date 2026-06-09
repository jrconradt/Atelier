using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Atelier.Framework.Generators.Requisition;

namespace Atelier.Framework.Infrastructure.Generators;

[Generator]
public sealed class FacilityGatewaySourceGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor NonOutcomeAuthenticatedMethodRule = new DiagnosticDescriptor(
        "ATELIER0700",
        "Authenticated facility method must return Outcome",
        "Method '{0}' on authenticated facility '{1}' does not return Outcome or Outcome<T>; it would bypass AuthorizeAsync. Change the return type to Outcome/Outcome<T>, or set [Facility(... , RequiresAuthentication = false)] / AllowAnonymous = true.",
        "Security",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Methods on a facility interface that requires authentication must return Outcome or Outcome<T> so the generated gateway routes them through AuthorizeAsync. Non-Outcome returns cannot be auth-checked and are refused at compile time.",
        helpLinkUri: "https://github.com/atelier-framework/atelier-references/wiki/ATELIER0700",
        customTags: new[] { WellKnownDiagnosticTags.Compiler });

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var gateways = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => IsCandidate(node),
                static (ctx, _) => Transform(ctx))
            .Where(static result => result is not null)
            .Select(static (result, _) => result!);

        context.RegisterSourceOutput(
            gateways,
            static (spc, result) =>
            {
                foreach (var diagnostic in result.Diagnostics)
                {
                    spc.ReportDiagnostic(diagnostic);
                }

                spc.AddSource(
                    result.HintName,
                    SourceText.From(result.Source, Encoding.UTF8));
            });
    }

    private static bool IsCandidate(SyntaxNode node)
    {
        if (node is not InterfaceDeclarationSyntax interfaceDeclaration)
        {
            return false;
        }

        return interfaceDeclaration.AttributeLists
            .SelectMany(al => al.Attributes)
            .Any(attr => attr.Name.ToString() is "Facility" or "FacilityAttribute");
    }

    private static FacilityGatewayResult? Transform(GeneratorSyntaxContext ctx)
    {
        var interfaceDeclaration = (InterfaceDeclarationSyntax)ctx.Node;

        if (ctx.SemanticModel.GetDeclaredSymbol(interfaceDeclaration) is not INamedTypeSymbol interfaceSymbol)
        {
            return null;
        }

        if (!HasFacilityAttribute(interfaceSymbol))
        {
            return null;
        }

        var diagnostics = new List<Diagnostic>();
        var generated = GenerateGateway(interfaceSymbol, diagnostics);
        var className = SymbolNaming.ImplName(interfaceSymbol.Name);
        return new FacilityGatewayResult(
            $"{className}.Gateway.g.cs",
            generated,
            diagnostics.ToImmutableArray());
    }

    private static bool HasFacilityAttribute(INamedTypeSymbol interfaceSymbol)
    {
        return interfaceSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.Name == "FacilityAttribute");
    }

    private static string GenerateGateway(INamedTypeSymbol interfaceSymbol, List<Diagnostic> diagnostics)
    {
        var namespaceName = interfaceSymbol.ContainingNamespace.ToDisplayString();
        var interfaceName = DisplayName(interfaceSymbol);
        var className = SymbolNaming.ImplName(interfaceSymbol.Name);

        var (requiresAuth, requiredClaims, requiredScopes) = ReadFacilityAuth(interfaceSymbol);

        var members = new List<string>
        {
            $"    [Requisite] private readonly {interfaceName} _backend = null!;"
        };

        if (requiresAuth)
        {
            members.Add("    [Requisite] private readonly global::Atelier.Framework.Identity.Interfaces.IJwtTokenValidator _tokenValidator = null!;");
            members.Add(BuildAuthMethod(requiredClaims, requiredScopes));
        }

        var ordinaryMethods = interfaceSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Ordinary)
            .ToList();

        foreach (var method in ordinaryMethods)
        {
            if (requiresAuth
                && !IsOutcomeOfT(method.ReturnType)
                && !IsBareOutcome(method.ReturnType))
            {
                diagnostics.Add(Diagnostic.Create(
                    NonOutcomeAuthenticatedMethodRule,
                    method.Locations.FirstOrDefault() ?? interfaceSymbol.Locations.FirstOrDefault() ?? Location.None,
                    method.Name,
                    interfaceSymbol.Name));
                continue;
            }

            members.Add(BuildMethod(method));
        }

        var body = string.Join("\n\n", members);

        return $$"""
            using System.Linq;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Atelier.Framework.Attributes;
            using Atelier.Framework.Primitives;
            using Atelier.Framework.Observability;
            using Atelier.Framework.Offering;
            using Atelier.Framework.Offering.Attributes;
            using Atelier.Framework.Outcomes;
            using Atelier.Framework.Requisitions;

            namespace {{namespaceName}};

            [Offering]
            [Infrastructure(InfrastructureLifetime.Scoped)]
            public sealed partial class {{className}} : global::Atelier.Framework.Offering.GatewayBase, {{interfaceName}}
            {
            {{body}}
            }

            """;
    }

    private static (bool RequiresAuth, string[] Claims, string[] Scopes) ReadFacilityAuth(INamedTypeSymbol interfaceSymbol)
    {
        var attribute = interfaceSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "FacilityAttribute");

        if (attribute is null)
        {
            return (false, System.Array.Empty<string>(), System.Array.Empty<string>());
        }

        var requiresAuthentication = true;
        var allowAnonymous = false;
        var claims = System.Array.Empty<string>();
        var scopes = System.Array.Empty<string>();

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
            else if (argument.Key == "RequiredClaims" && !argument.Value.IsNull)
            {
                claims = argument.Value.Values
                    .Select(v => v.Value?.ToString() ?? string.Empty)
                    .Where(s => s.Length > 0)
                    .ToArray();
            }
            else if (argument.Key == "RequiredScopes" && !argument.Value.IsNull)
            {
                scopes = argument.Value.Values
                    .Select(v => v.Value?.ToString() ?? string.Empty)
                    .Where(s => s.Length > 0)
                    .ToArray();
            }
        }

        return (requiresAuthentication && !allowAnonymous, claims, scopes);
    }

    private static string BuildAuthMethod(string[] requiredClaims, string[] requiredScopes)
    {
        var lines = new List<string>
        {
            "    protected override global::System.Threading.Tasks.Task<global::Atelier.Framework.Outcomes.Outcome> AuthorizeAsync()",
            "    {",
            "        if (!Context.TryGetValue(\"Authorization\", out var header) || string.IsNullOrWhiteSpace(header))",
            "        {",
            "            return global::System.Threading.Tasks.Task.FromResult(global::Atelier.Framework.Outcomes.Outcome.Failure());",
            "        }",
            "",
            "        var token = header.StartsWith(\"Bearer \", global::System.StringComparison.OrdinalIgnoreCase) ? header.Substring(7) : header;",
            "        var validation = _tokenValidator.Validate(token);",
            "        if (!validation.IsSuccess)",
            "        {",
            "            return global::System.Threading.Tasks.Task.FromResult(global::Atelier.Framework.Outcomes.Outcome.Failure());",
            "        }"
        };

        foreach (var claim in requiredClaims)
        {
            var claimLiteral = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(claim, quote: true);
            lines.Add("");
            lines.Add($"        if (!validation.Data!.Claims.Any(claim => claim.Type == {claimLiteral}))");
            lines.Add("        {");
            lines.Add("            return global::System.Threading.Tasks.Task.FromResult(global::Atelier.Framework.Outcomes.Outcome.Failure());");
            lines.Add("        }");
        }

        if (requiredScopes.Length > 0)
        {
            lines.Add("");
            lines.Add("        var grantedScopes = validation.Data!.Claims");
            lines.Add("            .Where(entry => entry.Type == \"scope\" || entry.Type == \"scp\")");
            lines.Add("            .SelectMany(entry => entry.Value.Split(' ', global::System.StringSplitOptions.RemoveEmptyEntries))");
            lines.Add("            .ToHashSet();");

            foreach (var scope in requiredScopes)
            {
                var scopeLiteral = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(scope, quote: true);
                lines.Add("");
                lines.Add($"        if (!grantedScopes.Contains({scopeLiteral}))");
                lines.Add("        {");
                lines.Add("            return global::System.Threading.Tasks.Task.FromResult(global::Atelier.Framework.Outcomes.Outcome.Failure());");
                lines.Add("        }");
            }
        }

        lines.Add("");
        lines.Add("        ApplyPrincipal(validation.Data!);");
        lines.Add("");
        lines.Add("        return global::System.Threading.Tasks.Task.FromResult(global::Atelier.Framework.Outcomes.Outcome.Success());");
        lines.Add("    }");

        return string.Join("\n", lines);
    }

    private static string BuildMethod(IMethodSymbol method)
    {
        var returnType = DisplayName(method.ReturnType);
        var methodName = method.Name;

        var parameters = string.Join(
            ", ",
            method.Parameters.Select(p => $"{DisplayName(p.Type)} {p.Name}"));

        var arguments = string.Join(", ", method.Parameters.Select(p => p.Name));

        if (IsOutcomeOfT(method.ReturnType))
        {
            var validateResponse = BuildResponseValidator(method);

            return $$"""
                    public async {{returnType}} {{methodName}}({{parameters}})
                    {
                        return await ForwardAsync(
                            nameof({{methodName}}),
                            {{validateResponse}},
                            () => _backend.{{methodName}}({{arguments}}));
                    }
                """;
        }

        if (IsBareOutcome(method.ReturnType))
        {
            return $$"""
                    public async {{returnType}} {{methodName}}({{parameters}})
                    {
                        return await ForwardAsync(
                            nameof({{methodName}}),
                            () => _backend.{{methodName}}({{arguments}}));
                    }
                """;
        }

        return $$"""
                public {{returnType}} {{methodName}}({{parameters}})
                {
                    return _backend.{{methodName}}({{arguments}});
                }
            """;
    }

    private static string BuildResponseValidator(IMethodSymbol method)
    {
        if (GetOutcomeInnerType(method.ReturnType) is INamedTypeSymbol inner
            && inner.GetAttributes().Any(a => a.AttributeClass?.Name == "ContractAttribute"))
        {
            var validatorType = $"global::{inner.ContainingNamespace.ToDisplayString()}.{inner.Name}ContractValidationExtensions";
            var outcomeType = $"global::Atelier.Framework.Outcomes.Outcome<{DisplayName(inner)}>";

            return $"response => response.Data is not null && !{validatorType}.IsValid(response.Data) ? {outcomeType}.Failure() : response";
        }

        return "response => response";
    }

    private static ITypeSymbol? GetOutcomeInnerType(ITypeSymbol returnType)
    {
        if (UnwrapTask(returnType) is INamedTypeSymbol named
            && named.IsGenericType
            && named.ConstructedFrom.Name == "Outcome")
        {
            return named.TypeArguments[0];
        }

        return null;
    }

    private static bool IsOutcomeOfT(ITypeSymbol returnType)
    {
        return UnwrapTask(returnType) is INamedTypeSymbol named
            && named.IsGenericType
            && named.ConstructedFrom.Name == "Outcome";
    }

    private static bool IsBareOutcome(ITypeSymbol returnType)
    {
        return UnwrapTask(returnType) is INamedTypeSymbol named
            && !named.IsGenericType
            && named.Name == "Outcome";
    }

    private static ITypeSymbol UnwrapTask(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named
            && named.IsGenericType
            && (named.ConstructedFrom.Name == "Task" || named.ConstructedFrom.Name == "ValueTask"))
        {
            return named.TypeArguments[0];
        }

        return type;
    }

    private static string DisplayName(ITypeSymbol type)
    {
        var format = new SymbolDisplayFormat(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
                                  SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

        return type.ToDisplayString(format);
    }

}

internal sealed record FacilityGatewayResult(
    string HintName,
    string Source,
    ImmutableArray<Diagnostic> Diagnostics);
