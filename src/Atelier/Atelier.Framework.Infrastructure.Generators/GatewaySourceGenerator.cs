using Templar.Rendering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Atelier.Framework.Generators.Requisition;
using G = Atelier.Framework.Infrastructure.Generators.Templates.Gateway;
using S = Atelier.Framework.Infrastructure.Generators.Compositors.Gateway.StrategyCalls;
using GT = Atelier.Framework.Infrastructure.Generators.Templates;

namespace Atelier.Framework.Infrastructure.Generators;

[Generator]
public sealed class GatewaySourceGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor MissingGatewayDomainsRule = new DiagnosticDescriptor(
        "ATELIER0701",
        "Domain gateway must specify source and target domains",
        "Gateway interface '{0}' is decorated with [DomainGateway] but does not supply both the source and target domain positional arguments",
        "Generator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The [DomainGateway] attribute requires a source domain and a target domain as its first two constructor arguments so the generated gateway can route between them.",
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
                if (result.Diagnostic is not null)
                {
                    spc.ReportDiagnostic(result.Diagnostic);
                    return;
                }

                spc.AddSource(result.HintName!,
                              SourceText.From(result.Source!, System.Text.Encoding.UTF8));
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
            .Any(attr => attr.Name.ToString() is "DomainGateway" or "DomainGatewayAttribute");
    }

    private static GatewayResult? Transform(GeneratorSyntaxContext ctx)
    {
        var interfaceDeclaration = (InterfaceDeclarationSyntax)ctx.Node;
        var symbol = ctx.SemanticModel.GetDeclaredSymbol(interfaceDeclaration);

        if (symbol is not INamedTypeSymbol interfaceSymbol)
        {
            return null;
        }

        var gatewayAttribute = GetGatewayAttribute(interfaceSymbol);
        if (gatewayAttribute == null)
        {
            return null;
        }

        if (gatewayAttribute.ConstructorArguments.Length < 2)
        {
            return GatewayResult.FromDiagnostic(Diagnostic.Create(
                MissingGatewayDomainsRule,
                interfaceSymbol.Locations.FirstOrDefault() ?? Location.None,
                interfaceSymbol.Name));
        }

        var sourceDomain = gatewayAttribute.ConstructorArguments[0].Value?.ToString() ?? "Unknown";
        var targetDomain = gatewayAttribute.ConstructorArguments[1].Value?.ToString() ?? "Unknown";

        string? strategyTypeName = null;
        var strategyArg = gatewayAttribute.NamedArguments
            .FirstOrDefault(kvp => kvp.Key == "StrategyType");
        if (strategyArg.Value.Value != null)
        {
            strategyTypeName = strategyArg.Value.Value.ToString();
        }

        var generatedCode = GenerateGatewayImplementation(
            interfaceSymbol,
            sourceDomain,
            targetDomain,
            strategyTypeName);

        var className = SymbolNaming.ImplName(interfaceSymbol.Name);
        var fileName = $"{className}.g.cs";

        return GatewayResult.FromSource(fileName, generatedCode);
    }

    private static AttributeData? GetGatewayAttribute(INamedTypeSymbol interfaceSymbol)
    {
        return interfaceSymbol.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.Name == "DomainGatewayAttribute");
    }

    private static string GenerateGatewayImplementation(
        INamedTypeSymbol interfaceSymbol,
        string sourceDomain,
        string targetDomain,
        string? strategyTypeName)
    {
        var namespaceName = interfaceSymbol.ContainingNamespace.ToDisplayString();
        var interfaceName = interfaceSymbol.Name;
        var className = SymbolNaming.ImplName(interfaceName);
        var hasStrategy = !string.IsNullOrEmpty(strategyTypeName);

        Compositor? strategyField = hasStrategy
            ? new G.StrategyField { StrategyType = strategyTypeName! }
            : null;

        var methodBlocks = Sequence.BlankLines(interfaceSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Ordinary)
            .Select(m => (Compositor)BuildMethod(m, hasStrategy)));

        return new G.Implementation
        {
            NamespaceName = namespaceName,
            ClassName = className,
            InterfaceName = interfaceName,
            StrategyField = strategyField,
            Methods = methodBlocks,
        }.Render();
    }

    private static Compositor BuildMethod(IMethodSymbol method, bool hasStrategy)
    {
        var returnType = FormatTypeWithGlobalPrefix(method.ReturnType);
        var methodName = method.Name;

        var parameters = Sequence.CommaList(method.Parameters.Select(p => (Compositor)new GT.ParameterFragment
        {
            ParamType = FormatTypeWithGlobalPrefix(p.Type),
            ParamName = p.Name,
            DefaultClause = p.HasExplicitDefaultValue ? " = " + ParameterFormatting.FormatDefaultValue(p) : string.Empty,
        })).Render();

        Compositor? metadataBlock = BuildMetadataBlock(method);
        Compositor callExpression = BuildStrategyCall(method, hasStrategy);

        return new G.Method
        {
            ReturnType = returnType,
            MethodName = methodName,
            Parameters = parameters,
            MetadataBlock = metadataBlock,
            CallExpression = callExpression,
        };
    }

    private static Compositor? BuildMetadataBlock(IMethodSymbol method)
    {
        var paramsForMetadata = method.Parameters
            .Where(p => p.Type.ToDisplayString() != "System.Threading.CancellationToken")
            .ToList();

        if (paramsForMetadata.Count == 0)
        {
            return null;
        }

        var entries = Sequence.Lines(paramsForMetadata.Select(p => (Compositor)new G.MetadataEntry { ParamName = p.Name }));

        return new G.MetadataBlock { Entries = entries };
    }

    private static Compositor BuildStrategyCall(IMethodSymbol method, bool hasStrategy)
    {
        if (hasStrategy)
        {
            var methodName = method.Name;

            if (methodName is "CreateScopeAsync" or "CreateEntityScopeAsync")
            {
                return new S.CreateScopeCase
                {
                    Arguments = BuildArgumentList(method.Parameters),
                };
            }

            if (methodName == "GetKnowledgeScopeAsync")
            {
                var sessionParam = method.Parameters.FirstOrDefault(p => p.Name == "sessionId");
                var ctParam = method.Parameters.FirstOrDefault(p => p.Type.Name == "CancellationToken");
                if (sessionParam != null && ctParam != null)
                {
                    return new S.GetKnowledgeScopeCase
                    {
                        SessionParam = sessionParam.Name,
                        CancellationParam = ctParam.Name,
                    };
                }
            }
            else if (methodName is "ValidateScopeAsync" or "ValidateQueryScopeAsync")
            {
                var scopeParam = method.Parameters.FirstOrDefault(p => p.Type.Name.Contains("ScopeData"));
                var cancelParam = method.Parameters.FirstOrDefault(p => p.Type.Name == "CancellationToken");
                if (scopeParam != null && cancelParam != null)
                {
                    return new S.ValidateScopeCase
                    {
                        ScopeParam = scopeParam.Name,
                        CancellationParam = cancelParam.Name,
                    };
                }
            }
            else if (methodName == "IsEntityAccessibleAsync")
            {
                return new S.IsEntityAccessibleCase
                {
                    Arguments = BuildArgumentList(method.Parameters),
                };
            }
        }

        return new S.DefaultCase
        {
            MethodName = method.Name,
            Arguments = BuildArgumentList(method.Parameters),
        };
    }

    private static Sequence BuildArgumentList(System.Collections.Immutable.ImmutableArray<IParameterSymbol> parameters)
    {
        return Sequence.CommaList(parameters.Select(p => (Compositor)new GT.IdentFragment { Text = p.Name }));
    }

    private static string FormatTypeWithGlobalPrefix(ITypeSymbol type)
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

internal sealed record GatewayResult(string? HintName, string? Source, Diagnostic? Diagnostic)
{
    public static GatewayResult FromSource(string hintName, string source)
    {
        return new GatewayResult(hintName, source, null);
    }

    public static GatewayResult FromDiagnostic(Diagnostic diagnostic)
    {
        return new GatewayResult(null, null, diagnostic);
    }
}
