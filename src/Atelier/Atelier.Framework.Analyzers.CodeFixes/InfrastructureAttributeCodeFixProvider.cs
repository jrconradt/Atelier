using Atelier.Framework.Primitives;
using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Atelier.Framework.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(InfrastructureAttributeCodeFixProvider)), Shared]
public sealed class InfrastructureAttributeCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create("ATELIER1402");

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

        var classDeclaration = AttributeCodeFixHelper
            .FindTargetDeclaration<ClassDeclarationSyntax>(root, diagnosticSpan);

        if (classDeclaration == null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Add [Infrastructure(InfrastructureLifetime.Scoped)]",
                createChangedDocument: c => AddInfrastructureAttributeAsync(
                    context.Document,
                    classDeclaration,
                    "Scoped",
                    c),
                equivalenceKey: "AddInfrastructureScoped"),
            diagnostic);

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Add [Infrastructure(InfrastructureLifetime.Singleton)]",
                createChangedDocument: c => AddInfrastructureAttributeAsync(
                    context.Document,
                    classDeclaration,
                    "Singleton",
                    c),
                equivalenceKey: "AddInfrastructureSingleton"),
            diagnostic);

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Add [Infrastructure(InfrastructureLifetime.Transient)]",
                createChangedDocument: c => AddInfrastructureAttributeAsync(
                    context.Document,
                    classDeclaration,
                    "Transient",
                    c),
                equivalenceKey: "AddInfrastructureTransient"),
            diagnostic);
    }

    private static async Task<Document> AddInfrastructureAttributeAsync(
        Document document,
        ClassDeclarationSyntax classDeclaration,
        string lifetime,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
        {
            return document;
        }

        var attribute = SyntaxFactory.Attribute(
            SyntaxFactory.ParseName("Infrastructure"),
            SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.AttributeArgument(
                        SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.IdentifierName("InfrastructureLifetime"),
                            SyntaxFactory.IdentifierName(lifetime))))));

        var newRoot = AttributeCodeFixHelper.AddAttributeAndEnsureUsing(
            root,
            classDeclaration,
            attribute,
            "Atelier.Framework.Attributes");

        return document.WithSyntaxRoot(newRoot);
    }
}
