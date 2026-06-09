using Atelier.Framework.Primitives;
using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Atelier.Framework.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RequisiteDependencyCodeFixProvider)), Shared]
public sealed class RequisiteDependencyCodeFixProvider : CodeFixProvider
{
    private const string ADD_INFRASTRUCTURE_ATTRIBUTE_TITLE = "Add [Infrastructure] attribute to type";
    private const string ADD_MANUAL_REGISTRATION_TITLE = "Add manual DI registration";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create("ATELIER0600", "ATELIER0601");

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);

        if (root == null)
        {
            return;
        }

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var node = root.FindNode(diagnosticSpan);

        if (node == null)
        {
            return;
        }

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken)
            .ConfigureAwait(false);

        if (semanticModel == null)
        {
            return;
        }

        ITypeSymbol? targetType = null;

        if (node is VariableDeclaratorSyntax declarator &&
            declarator.Parent?.Parent is FieldDeclarationSyntax fieldDeclaration)
        {
            var typeInfo = semanticModel.GetTypeInfo(fieldDeclaration.Declaration.Type, context.CancellationToken);
            targetType = typeInfo.Type;
        }
        else if (node is ParameterSyntax parameter)
        {
            var typeInfo = semanticModel.GetTypeInfo(parameter.Type!, context.CancellationToken);
            targetType = typeInfo.Type;
        }

        if (targetType == null || targetType is IErrorTypeSymbol)
        {
            return;
        }

        var namedType = targetType as INamedTypeSymbol;
        if (namedType == null)
        {
            return;
        }

        if (namedType.TypeKind == TypeKind.Interface)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Navigate to interface implementation",
                    createChangedDocument: _ => Task.FromResult(context.Document),
                    equivalenceKey: nameof(RequisiteDependencyCodeFixProvider) + "_Navigate"),
                diagnostic);
        }
        else if (namedType.TypeKind == TypeKind.Class)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: ADD_INFRASTRUCTURE_ATTRIBUTE_TITLE,
                    createChangedSolution: c => AddInfrastructureAttributeAsync(
                        context.Document.Project.Solution,
                        namedType,
                        c),
                    equivalenceKey: nameof(RequisiteDependencyCodeFixProvider) + "_AddAttribute"),
                diagnostic);
        }
    }

    private async Task<Solution> AddInfrastructureAttributeAsync(
        Solution solution,
        INamedTypeSymbol typeSymbol,
        CancellationToken cancellationToken)
    {
        var syntaxRef = typeSymbol.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxRef == null)
        {
            return solution;
        }

        var document = solution.GetDocument(syntaxRef.SyntaxTree);
        if (document == null)
        {
            return solution;
        }

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
        {
            return solution;
        }

        var classDeclaration = await syntaxRef.GetSyntaxAsync(cancellationToken).ConfigureAwait(false)
            as ClassDeclarationSyntax;

        if (classDeclaration == null)
        {
            return solution;
        }

        var lifetime = InferLifetime(typeSymbol);

        var attributeList = SyntaxFactory.AttributeList(
            SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.Attribute(
                    SyntaxFactory.IdentifierName("Infrastructure"),
                    SyntaxFactory.AttributeArgumentList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.AttributeArgument(
                                SyntaxFactory.MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    SyntaxFactory.IdentifierName("InfrastructureLifetime"),
                                    SyntaxFactory.IdentifierName(lifetime))))))));

        var newClassDeclaration = classDeclaration.AddAttributeLists(attributeList);

        var newRoot = root.ReplaceNode(classDeclaration, newClassDeclaration);

        newRoot = AttributeCodeFixHelper.EnsureUsing(
            newRoot,
            "Atelier.Framework.Attributes");

        return document.WithSyntaxRoot(newRoot).Project.Solution;
    }

    private string InferLifetime(INamedTypeSymbol typeSymbol)
    {
        var className = typeSymbol.Name;

        if (className.EndsWith("Repository", StringComparison.OrdinalIgnoreCase) ||
            className.EndsWith("DbContext", StringComparison.OrdinalIgnoreCase) ||
            className.EndsWith("UnitOfWork", StringComparison.OrdinalIgnoreCase))
        {
            return "Scoped";
        }

        var hasStatefulFields = typeSymbol.GetMembers()
            .OfType<IFieldSymbol>()
            .Any(f => !f.IsStatic &&
                     !f.IsReadOnly &&
                     !f.IsConst &&
                     !HasRequisiteAttribute(f));

        if (hasStatefulFields)
        {
            return "Scoped";
        }

        return "Singleton";
    }

    private static bool HasRequisiteAttribute(ISymbol symbol)
    {
        return symbol.GetAttributes()
            .Any(a => a.AttributeClass?.Name == "RequisiteAttribute");
    }
}
