using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Atelier.Framework.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RedundantConstructorCodeFixProvider)), Shared]
public sealed class RedundantConstructorCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(RedundantConstructorAnalyzer.DIAGNOSTIC_ID);

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

        var constructor = root.FindToken(diagnosticSpan.Start)
            .Parent?
            .AncestorsAndSelf()
            .OfType<ConstructorDeclarationSyntax>()
            .FirstOrDefault();

        if (constructor == null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Remove redundant constructor (generator will create it)",
                createChangedDocument: c => RemoveConstructorAsync(
                    context.Document,
                    constructor,
                    c),
                equivalenceKey: "RemoveRedundantConstructor"),
            diagnostic);
    }

    private static async Task<Document> RemoveConstructorAsync(
        Document document,
        ConstructorDeclarationSyntax constructor,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
        {
            return document;
        }

        var newRoot = root.RemoveNode(constructor, SyntaxRemoveOptions.KeepNoTrivia);
        if (newRoot == null)
        {
            return document;
        }

        return document.WithSyntaxRoot(newRoot);
    }
}
