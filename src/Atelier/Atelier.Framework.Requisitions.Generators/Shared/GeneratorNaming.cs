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
}
