using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Atelier.Framework.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(OperationAttributeCodeFixProvider)), Shared]
public sealed class OperationAttributeCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create("ATELIER1404");

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

        var methodDeclaration = AttributeCodeFixHelper
            .FindTargetDeclaration<MethodDeclarationSyntax>(root, diagnosticSpan);

        if (methodDeclaration == null)
        {
            return;
        }

        var methodName = methodDeclaration.Identifier.Text;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Add [Operation(\"{methodName}\")]",
                createChangedDocument: c => AddOperationAttributeAsync(
                    context.Document,
                    methodDeclaration,
                    methodName,
                    c),
                equivalenceKey: "AddOperation"),
            diagnostic);
    }

    private static async Task<Document> AddOperationAttributeAsync(
        Document document,
        MethodDeclarationSyntax methodDeclaration,
        string operationName,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
        {
            return document;
        }

        var nameArgument = SyntaxFactory.AttributeArgument(
            SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                SyntaxFactory.Literal(operationName)));

        var attribute = SyntaxFactory.Attribute(
            SyntaxFactory.ParseName("Operation"),
            SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SingletonSeparatedList(nameArgument)));

        var newRoot = AttributeCodeFixHelper.AddAttributeAndEnsureUsing(
            root,
            methodDeclaration,
            attribute,
            "Atelier.Framework.Operation");

        return document.WithSyntaxRoot(newRoot);
    }
}
