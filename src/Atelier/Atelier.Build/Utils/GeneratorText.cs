using System.Text.RegularExpressions;

namespace Atelier.Build.Utils;

public static class GeneratorText
{
    private static readonly Regex TypeNamePattern =
        new(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$", RegexOptions.Compiled);

    private static readonly Regex HealthPathPattern =
        new("^/[A-Za-z0-9._~/-]*$", RegexOptions.Compiled);

    public static bool IsValidTypeName(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }
        return TypeNamePattern.IsMatch(value);
    }

    public static string SanitizeScalar(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }
        return value
            .Replace("\r", string.Empty)
            .Replace("\n", string.Empty);
    }

    public static string SanitizeHealthPath(string? value)
    {
        var sanitized = SanitizeScalar(value);
        if (string.IsNullOrEmpty(sanitized)
            || !HealthPathPattern.IsMatch(sanitized))
        {
            return "/health";
        }
        return sanitized;
    }

    public static string EscapeQuotedScalar(string? value)
    {
        return SanitizeScalar(value)
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");
    }

    public static string EscapeCSharpLiteral(string? value)
    {
        return SanitizeScalar(value)
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");
    }
}
