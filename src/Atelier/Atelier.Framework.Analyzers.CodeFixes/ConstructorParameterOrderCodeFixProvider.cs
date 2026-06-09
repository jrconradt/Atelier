using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Atelier.Framework.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ConstructorParameterOrderCodeFixProvider)), Shared]
public sealed class ConstructorParameterOrderCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create("CS1503", "CS1729", "CS7036");

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);

        if (root == null)
        {
            return;
        }

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var constructorDeclaration = root.FindToken(diagnosticSpan.Start)
            .Parent?
            .AncestorsAndSelf()
            .OfType<ConstructorDeclarationSyntax>()
            .FirstOrDefault();

        if (constructorDeclaration?.Initializer == null)
        {
            return;
        }

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken)
            .ConfigureAwait(false);

        if (semanticModel == null)
        {
            return;
        }

        var constructorSymbol = semanticModel.GetDeclaredSymbol(constructorDeclaration, context.CancellationToken);
        if (constructorSymbol?.ContainingType.BaseType == null)
        {
            return;
        }

        var baseConstructors = constructorSymbol.ContainingType.BaseType.Constructors
            .Where(c => !c.IsStatic && c.DeclaredAccessibility != Accessibility.Private)
            .ToList();

        if (baseConstructors.Count == 0)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Reorder constructor parameters to match base class",
                createChangedDocument: c => ReorderConstructorParametersAsync(
                    context.Document,
                    constructorDeclaration,
                    baseConstructors,
                    semanticModel,
                    c),
                equivalenceKey: "ReorderConstructorParameters"),
            diagnostic);
    }

    private static async Task<Document> ReorderConstructorParametersAsync(
        Document document,
        ConstructorDeclarationSyntax constructorDeclaration,
        List<IMethodSymbol> baseConstructors,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null || constructorDeclaration.Initializer == null)
        {
            return document;
        }

        var currentArguments = constructorDeclaration.Initializer.ArgumentList.Arguments;

        var argumentTypes = currentArguments
            .Select(arg => semanticModel.GetTypeInfo(arg.Expression, cancellationToken).Type)
            .ToList();

        IMethodSymbol? bestMatch = null;
        foreach (var baseCtor in baseConstructors)
        {
            if (baseCtor.Parameters.Length != argumentTypes.Count)
            {
                continue;
            }

            bool allMatch = true;
            for (int i = 0; i < argumentTypes.Count; i++)
            {
                if (argumentTypes[i] == null)
                {
                    continue;
                }

                var conversion = semanticModel.Compilation.ClassifyConversion(
                    argumentTypes[i]!,
                    baseCtor.Parameters[i].Type);

                if (!conversion.Exists)
                {
                    allMatch = false;
                    break;
                }
            }

            if (allMatch)
            {
                bestMatch = baseCtor;
                break;
            }
        }

        if (bestMatch == null)
        {
            return document;
        }

        var reorderedArguments = new ArgumentSyntax[currentArguments.Count];
        var usedArguments = new bool[currentArguments.Count];

        for (int paramIndex = 0; paramIndex < bestMatch.Parameters.Length; paramIndex++)
        {
            var paramType = bestMatch.Parameters[paramIndex].Type;

            for (int argIndex = 0; argIndex < argumentTypes.Count; argIndex++)
            {
                if (usedArguments[argIndex] || argumentTypes[argIndex] == null)
                {
                    continue;
                }

                var conversion = semanticModel.Compilation.ClassifyConversion(
                    argumentTypes[argIndex]!,
                    paramType);

                if (conversion.IsIdentity || (conversion.IsImplicit && conversion.IsReference))
                {
                    reorderedArguments[paramIndex] = currentArguments[argIndex];
                    usedArguments[argIndex] = true;
                    break;
                }
            }
        }

        if (reorderedArguments.Any(a => a == null))
        {
            return document;
        }

        var newArgumentList = SyntaxFactory.ArgumentList(
            SyntaxFactory.SeparatedList(reorderedArguments));

        var newInitializer = constructorDeclaration.Initializer
            .WithArgumentList(newArgumentList);

        var newConstructor = constructorDeclaration.WithInitializer(newInitializer);
        var newRoot = root.ReplaceNode(constructorDeclaration, newConstructor);

        return document.WithSyntaxRoot(newRoot);
    }
}
