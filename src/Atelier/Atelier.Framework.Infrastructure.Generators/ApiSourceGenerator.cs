using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Atelier.Framework.Infrastructure.Generators;

[Generator]
public class ApiSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var endpoints = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => IsCandidate(node),
                static (ctx, _) => Transform(ctx))
            .Where(static result => result is not null)
            .Select(static (result, _) => result!);

        context.RegisterSourceOutput(
            endpoints,
            static (spc, result) =>
                spc.AddSource(result.HintName,
                              SourceText.From(result.Source, Encoding.UTF8)));
    }

    private static bool IsCandidate(SyntaxNode node)
    {
        if (node is not ClassDeclarationSyntax classDeclaration)
        {
            return false;
        }

        return classDeclaration.AttributeLists
            .SelectMany(al => al.Attributes)
            .Any(attr => attr.Name.ToString() is "Api" or "ApiAttribute");
    }

    private static ApiEndpointResult? Transform(GeneratorSyntaxContext ctx)
    {
        var controllerClass = (ClassDeclarationSyntax)ctx.Node;
        if (ctx.SemanticModel.GetDeclaredSymbol(controllerClass) is not INamedTypeSymbol classSymbol)
        {
            return null;
        }

        var source = new ApiEndpointBuilder(classSymbol).Build();
        if (string.IsNullOrEmpty(source))
        {
            return null;
        }

        var namespacePart = classSymbol.ContainingNamespace.ToDisplayString().Replace(".", "_");
        return new ApiEndpointResult($"{namespacePart}_{classSymbol.Name}_ApiEndpoints.g.cs", source);
    }
}

internal sealed record ApiEndpointResult(string HintName, string Source);
