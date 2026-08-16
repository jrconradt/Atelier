using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Atelier.Framework.Generators.Requisition;
using Templar.Rendering;
using FT = Atelier.Framework.Infrastructure.Generators.Compositors.Facility;

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

                foreach (var file in result.Files)
                {
                    spc.AddSource(
                        file.HintName,
                        SourceText.From(file.Source, Encoding.UTF8));
                }
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
        var (requiresAuth, requiredClaims, requiredScopes, facilityName) = ReadFacilityAuth(interfaceSymbol);
        var gatewaySource = GenerateGateway(interfaceSymbol, requiresAuth, requiredClaims, requiredScopes, diagnostics);
        var clientSource = GenerateClient(interfaceSymbol, facilityName);
        var endpointsSource = GenerateEndpoints(interfaceSymbol, facilityName);

        var implName = SymbolNaming.ImplName(interfaceSymbol.Name);
        var files = ImmutableArray.Create(
            new GeneratedFile($"{implName}.Gateway.g.cs", gatewaySource),
            new GeneratedFile($"{implName}.Client.g.cs", clientSource),
            new GeneratedFile($"{implName}.Endpoints.g.cs", endpointsSource)
        );

        return new FacilityGatewayResult(files, diagnostics.ToImmutableArray());
    }

    private static bool HasFacilityAttribute(INamedTypeSymbol interfaceSymbol)
    {
        return interfaceSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.Name == "FacilityAttribute");
    }

    private static string GenerateGateway(
        INamedTypeSymbol interfaceSymbol,
        bool requiresAuth,
        string[] requiredClaims,
        string[] requiredScopes,
        List<Diagnostic> diagnostics)
    {
        return new FT.Gateway(interfaceSymbol, requiresAuth, requiredClaims, requiredScopes, diagnostics, NonOutcomeAuthenticatedMethodRule).Render();
    }

    internal static ITypeSymbol? GetOutcomeInnerType(ITypeSymbol returnType)
    {
        if (UnwrapTask(returnType) is INamedTypeSymbol named
            && named.IsGenericType
            && named.ConstructedFrom.Name == "Outcome")
        {
            return named.TypeArguments[0];
        }

        return null;
    }

    internal static bool IsOutcomeOfT(ITypeSymbol returnType)
    {
        return UnwrapTask(returnType) is INamedTypeSymbol named
            && named.IsGenericType
            && named.ConstructedFrom.Name == "Outcome";
    }

    internal static bool IsBareOutcome(ITypeSymbol returnType)
    {
        return UnwrapTask(returnType) is INamedTypeSymbol named
            && !named.IsGenericType
            && named.Name == "Outcome";
    }

    internal static ITypeSymbol UnwrapTask(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named
            && named.IsGenericType
            && (named.ConstructedFrom.Name == "Task" || named.ConstructedFrom.Name == "ValueTask"))
        {
            return named.TypeArguments[0];
        }

        return type;
    }

    internal static string DisplayName(ITypeSymbol type)
    {
        var format = new SymbolDisplayFormat(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
                                  SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

        return type.ToDisplayString(format);
    }
    private static string GenerateClient(INamedTypeSymbol interfaceSymbol, string facilityName)
    {
        return new FT.Client(interfaceSymbol, facilityName).Render();
    }

    private static string GenerateEndpoints(INamedTypeSymbol interfaceSymbol, string facilityName)
    {
        return new FT.Endpoints(interfaceSymbol, facilityName).Render();
    }

    private static (bool RequiresAuth, string[] Claims, string[] Scopes, string FacilityName) ReadFacilityAuth(INamedTypeSymbol interfaceSymbol)
    {
        var attribute = interfaceSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "FacilityAttribute");

        var facilityName = attribute?.ConstructorArguments.Length > 0 ? attribute.ConstructorArguments[0].Value?.ToString() ?? interfaceSymbol.Name : interfaceSymbol.Name;

        if (attribute is null)
        {
            return (false, System.Array.Empty<string>(), System.Array.Empty<string>(), facilityName);
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

        return (requiresAuthentication && !allowAnonymous, claims, scopes, facilityName);
    }
}

internal sealed record GeneratedFile(string HintName, string Source);

internal sealed record FacilityGatewayResult(
    ImmutableArray<GeneratedFile> Files,
    ImmutableArray<Diagnostic> Diagnostics);
