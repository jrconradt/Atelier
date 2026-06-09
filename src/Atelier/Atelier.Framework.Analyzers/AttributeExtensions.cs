using Microsoft.CodeAnalysis;

namespace Atelier.Framework.Analyzers;

internal static class AttributeExtensions
{
    public static bool HasAttribute(this ISymbol? symbol, string attributeName)
    {
        if (symbol == null)
        {
            return false;
        }

        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == attributeName)
            {
                return true;
            }
        }

        return false;
    }

    public static AttributeData? FindAttribute(this ISymbol? symbol, string attributeName)
    {
        if (symbol == null)
        {
            return null;
        }

        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == attributeName)
            {
                return attribute;
            }
        }

        return null;
    }
}
