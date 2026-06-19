using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Atelier.Framework.Generators.Requisition;

internal static class GeneratorNaming
{
    public static string ToCamelCase(string value)
    {
        if (string.IsNullOrEmpty(value) || char.IsLower(value[0]))
        {
            return value;
        }

        if (value.StartsWith("_"))
        {
            value = value.Substring(1);
        }

        return char.ToLowerInvariant(value[0]) + value.Substring(1);
    }

    public static string ToParameterName(string value)
    {
        return EscapeKeyword(ToCamelCase(value));
    }

    public static string SanitizeNamespace(INamespaceSymbol namespaceSymbol)
    {
        return namespaceSymbol.ToDisplayString().Replace(".", "_");
    }

    private static string EscapeKeyword(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
        {
            return identifier;
        }

        if (SyntaxFacts.IsReservedKeyword(SyntaxFacts.GetKeywordKind(identifier)))
        {
            return "@" + identifier;
        }

        return identifier;
    }
}
