using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Atelier.Framework.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ContractAttributeCodeFixProvider)), Shared]
public sealed class ContractAttributeCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create("ATELIER0200");

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

        var className = classDeclaration.Identifier.Text;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Add [Contract(\"{className}\", Version = \"1.0\")]",
                createChangedDocument: c => AddContractAttributeAsync(
                    context.Document,
                    classDeclaration,
                    className,
                    c),
                equivalenceKey: "AddContract"),
            diagnostic);
    }

    private static async Task<Document> AddContractAttributeAsync(
        Document document,
        ClassDeclarationSyntax classDeclaration,
        string contractName,
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
                SyntaxFactory.Literal(contractName)));

        var versionArgument = SyntaxFactory.AttributeArgument(
            SyntaxFactory.NameEquals("Version"),
            null,
            SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                SyntaxFactory.Literal("1.0")));

        var attribute = SyntaxFactory.Attribute(
            SyntaxFactory.ParseName("Contract"),
            SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SeparatedList(new[]
                {
                    nameArgument,
                    versionArgument
                })));

        var newRoot = AttributeCodeFixHelper.AddAttributeAndEnsureUsing(
            root,
            classDeclaration,
            attribute,
            "Atelier.Framework.Attributes");

        return document.WithSyntaxRoot(newRoot);
    }
}
