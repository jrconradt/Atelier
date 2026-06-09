using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Atelier.Framework.Analyzers;

internal static class AttributeCodeFixHelper
{
    public static TDeclaration? FindTargetDeclaration<TDeclaration>(SyntaxNode root,
                                                                    TextSpan diagnosticSpan)
        where TDeclaration : SyntaxNode
    {
        return root.FindToken(diagnosticSpan.Start)
            .Parent?
            .AncestorsAndSelf()
            .OfType<TDeclaration>()
            .FirstOrDefault();
    }

    public static SyntaxNode AddAttributeAndEnsureUsing(SyntaxNode root,
                                                        MemberDeclarationSyntax declaration,
                                                        AttributeSyntax attribute,
                                                        string usingNamespace)
    {
        var attributeList = SyntaxFactory.AttributeList(
            SyntaxFactory.SingletonSeparatedList(attribute))
            .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

        var newDeclaration = declaration.AddAttributeLists(attributeList);

        var newRoot = root.ReplaceNode(declaration, newDeclaration);

        return EnsureUsing(newRoot, usingNamespace);
    }

    public static SyntaxNode EnsureUsing(SyntaxNode root,
                                         string usingNamespace)
    {
        if (root is not CompilationUnitSyntax compilationUnit)
        {
            return root;
        }

        var hasUsing = compilationUnit.Usings
            .Any(u => u.Name?.ToString() == usingNamespace);

        if (hasUsing)
        {
            return compilationUnit;
        }

        var usingDirective = SyntaxFactory.UsingDirective(
            SyntaxFactory.ParseName(usingNamespace))
            .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

        return compilationUnit.AddUsings(usingDirective);
    }
}
