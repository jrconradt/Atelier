using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Atelier.Framework.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(OperationNullValidationCodeFixProvider)), Shared]
public class OperationNullValidationCodeFixProvider : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create("ATELIER003");

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
        {
            return;
        }

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var methodDeclaration = root.FindToken(diagnosticSpan.Start)
            .Parent?
            .AncestorsAndSelf()
            .OfType<MethodDeclarationSyntax>()
            .First();

        if (methodDeclaration == null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Add null parameter validation",
                createChangedDocument: c => AddNullValidationAsync(context.Document, methodDeclaration, c),
                equivalenceKey: nameof(OperationNullValidationCodeFixProvider)),
            diagnostic);
    }

    private async Task<Document> AddNullValidationAsync(
        Document document,
        MethodDeclarationSyntax methodDeclaration,
        CancellationToken cancellationToken)
    {
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
        if (semanticModel == null)
        {
            return document;
        }

        var methodSymbol = semanticModel.GetDeclaredSymbol(methodDeclaration, cancellationToken);
        if (methodSymbol == null)
        {
            return document;
        }

        var parametersToValidate = methodSymbol.Parameters
            .Where(p => RequiresNullValidation(p))
            .ToList();

        if (parametersToValidate.Count == 0)
        {
            return document;
        }

        var outcomeType = ExtractOutcomeType(methodSymbol.ReturnType);

        var validationStatements = new List<StatementSyntax>();

        foreach (var parameter in parametersToValidate)
        {



            var condition = SyntaxFactory.BinaryExpression(
                SyntaxKind.EqualsExpression,
                SyntaxFactory.IdentifierName(parameter.Name),
                SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression));

            var returnStatement = SyntaxFactory.ReturnStatement(
                SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.ParseTypeName(outcomeType),
                        SyntaxFactory.IdentifierName("Failure")))
                .WithArgumentList(
                    SyntaxFactory.ArgumentList(
                        SyntaxFactory.SeparatedList(new[]
                        {
                            SyntaxFactory.Argument(
                                SyntaxFactory.LiteralExpression(
                                    SyntaxKind.StringLiteralExpression,
                                    SyntaxFactory.Literal($"Parameter '{parameter.Name}' cannot be null"))),
                            SyntaxFactory.Argument(
                                SyntaxFactory.LiteralExpression(
                                    SyntaxKind.StringLiteralExpression,
                                    SyntaxFactory.Literal("INVALID_PARAMETER")))
                        }))));

            var ifStatement = SyntaxFactory.IfStatement(
                condition,
                SyntaxFactory.Block(returnStatement))
                .WithLeadingTrivia(SyntaxFactory.Whitespace("        "))
                .WithTrailingTrivia(SyntaxFactory.EndOfLine("\n"));

            validationStatements.Add(ifStatement);
        }

        validationStatements.Add(
            SyntaxFactory.EmptyStatement()
                .WithTrailingTrivia(SyntaxFactory.EndOfLine("\n")));

        var oldBody = methodDeclaration.Body;
        if (oldBody == null)
        {
            return document;
        }

        var newStatements = validationStatements.Concat(oldBody.Statements);
        var newBody = oldBody.WithStatements(SyntaxFactory.List(newStatements));

        var newMethod = methodDeclaration.WithBody(newBody);

        var root = await document.GetSyntaxRootAsync(cancellationToken);
        if (root == null)
        {
            return document;
        }

        var newRoot = root.ReplaceNode(methodDeclaration, newMethod);
        return document.WithSyntaxRoot(newRoot);
    }

    private static bool RequiresNullValidation(IParameterSymbol parameter)
    {
        if (parameter.RefKind == RefKind.Out || parameter.RefKind == RefKind.Ref)
        {
            return false;
        }

        if (parameter.IsOptional || parameter.IsParams)
        {
            return false;
        }

        if (parameter.Type.IsValueType &&
            parameter.NullableAnnotation != NullableAnnotation.Annotated)
        {
            return false;
        }

        if (parameter.Type.ToDisplayString() == "System.Threading.CancellationToken")
        {
            return false;
        }

        if (parameter.NullableAnnotation == NullableAnnotation.Annotated)
        {
            return false;
        }

        return parameter.Type.SpecialType == SpecialType.System_String ||
               parameter.Type.TypeKind == TypeKind.Class ||
               parameter.Type.TypeKind == TypeKind.Interface ||
               parameter.Type.TypeKind == TypeKind.Delegate ||
               parameter.Type.TypeKind == TypeKind.Array;
    }

    private static string ExtractOutcomeType(ITypeSymbol returnType)
    {
        var unwrapped = UnwrapAwaitable(returnType);

        if (unwrapped is INamedTypeSymbol named && named.Name == "Outcome")
        {
            return named.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        }

        return "Outcome";
    }

    private static ITypeSymbol UnwrapAwaitable(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named
            && (named.Name == "Task" || named.Name == "ValueTask")
            && named.IsGenericType
            && named.TypeArguments.Length == 1)
        {
            return named.TypeArguments[0];
        }

        return type;
    }
}
