using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Atelier.Framework.Infrastructure.Generators;

internal static class ParameterFormatting
{
    public static string FormatDefaultValue(IParameterSymbol parameter)
    {
        var value = parameter.ExplicitDefaultValue;
        var type = parameter.Type;
        var qualifiedType = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        if (value is null)
        {
            if (type.IsReferenceType
                || type.TypeKind == TypeKind.Pointer)
            {
                return "null";
            }
            return $"default({qualifiedType})";
        }

        if (type.TypeKind == TypeKind.Enum
            && type is INamedTypeSymbol enumType)
        {
            return FormatEnumValue(enumType, qualifiedType, value);
        }

        return value switch
        {
            string s => SymbolDisplay.FormatLiteral(s, quote: true),
            bool b => b ? "true" : "false",
            char c => SymbolDisplay.FormatLiteral(c, quote: true),
            long l => $"{SymbolDisplay.FormatPrimitive(l, quoteStrings: false, useHexadecimalNumbers: false)}L",
            ulong ul => $"{SymbolDisplay.FormatPrimitive(ul, quoteStrings: false, useHexadecimalNumbers: false)}UL",
            uint ui => $"{SymbolDisplay.FormatPrimitive(ui, quoteStrings: false, useHexadecimalNumbers: false)}U",
            float f => $"{SymbolDisplay.FormatPrimitive(f, quoteStrings: false, useHexadecimalNumbers: false)}F",
            double d => $"{SymbolDisplay.FormatPrimitive(d, quoteStrings: false, useHexadecimalNumbers: false)}D",
            decimal m => $"{SymbolDisplay.FormatPrimitive(m, quoteStrings: false, useHexadecimalNumbers: false)}M",
            byte or sbyte or short or ushort or int => SymbolDisplay.FormatPrimitive(value, quoteStrings: false, useHexadecimalNumbers: false),
            _ => $"default({qualifiedType})",
        };
    }

    private static string FormatEnumValue(INamedTypeSymbol enumType, string qualifiedType, object value)
    {
        foreach (var member in enumType.GetMembers().OfType<IFieldSymbol>())
        {
            if (member.HasConstantValue
                && Equals(member.ConstantValue, value))
            {
                return $"{qualifiedType}.{member.Name}";
            }
        }

        var underlying = SymbolDisplay.FormatPrimitive(value, quoteStrings: false, useHexadecimalNumbers: false);
        return $"({qualifiedType}){underlying}";
    }
}
