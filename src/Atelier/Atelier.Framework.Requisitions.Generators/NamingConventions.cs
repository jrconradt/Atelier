using System.Text.RegularExpressions;

namespace Atelier.Framework.Templates;

public static class NamingConventions
{
    public static (string HttpMethod, string RoutePattern, bool HasIdParameter) InferEndpointDetails(
        string methodName,
        string resourceName)
    {
        if (methodName.StartsWith("Get", StringComparison.OrdinalIgnoreCase))
        {
            var remainder = methodName.Substring(3);

            if (string.IsNullOrEmpty(remainder) || remainder.Equals(resourceName, StringComparison.OrdinalIgnoreCase))
            {
                return ("Get", $"/{{{ToLowerCamelCase(resourceName)}Id}}", true);
            }

            if (remainder.Equals($"{resourceName}List", StringComparison.OrdinalIgnoreCase) ||
                remainder.Equals("All", StringComparison.OrdinalIgnoreCase) ||
                remainder.EndsWith("List", StringComparison.OrdinalIgnoreCase))
            {
                return ("Get", string.Empty, false);
            }

            var subResource = ExtractSubResource(remainder, resourceName);
            return ("Get", $"/{ToKebabCase(subResource)}", false);
        }

        if (methodName.StartsWith("List", StringComparison.OrdinalIgnoreCase))
        {
            return ("Get", string.Empty, false);
        }

        if (methodName.StartsWith("Create", StringComparison.OrdinalIgnoreCase) ||
            methodName.StartsWith("Post", StringComparison.OrdinalIgnoreCase))
        {
            var remainder = methodName.StartsWith("Create", StringComparison.OrdinalIgnoreCase)
                ? methodName.Substring(6)
                : methodName.Substring(4);

            if (string.IsNullOrEmpty(remainder) || remainder.Equals(resourceName, StringComparison.OrdinalIgnoreCase))
            {
                return ("Post", string.Empty, false);
            }

            var subResource = ExtractSubResource(remainder, resourceName);
            return ("Post", $"/{ToKebabCase(subResource)}", false);
        }

        if (methodName.StartsWith("Update", StringComparison.OrdinalIgnoreCase) ||
            methodName.StartsWith("Put", StringComparison.OrdinalIgnoreCase))
        {
            var remainder = methodName.StartsWith("Update", StringComparison.OrdinalIgnoreCase)
                ? methodName.Substring(6)
                : methodName.Substring(3);

            if (string.IsNullOrEmpty(remainder) || remainder.Equals(resourceName, StringComparison.OrdinalIgnoreCase))
            {
                return ("Put", $"/{{{ToLowerCamelCase(resourceName)}Id}}", true);
            }

            var subResource = ExtractSubResource(remainder, resourceName);
            return ("Put", $"/{{{ToLowerCamelCase(resourceName)}Id}}/{ToKebabCase(subResource)}", true);
        }

        if (methodName.StartsWith("Delete", StringComparison.OrdinalIgnoreCase) ||
            methodName.StartsWith("Remove", StringComparison.OrdinalIgnoreCase))
        {
            return ("Delete", $"/{{{ToLowerCamelCase(resourceName)}Id}}", true);
        }

        if (methodName.StartsWith("Patch", StringComparison.OrdinalIgnoreCase))
        {
            return ("Patch", $"/{{{ToLowerCamelCase(resourceName)}Id}}", true);
        }

        return ("Get", $"/{ToKebabCase(methodName)}", false);
    }

    public static string ExtractResourceName(string serviceName)
    {
        if (serviceName.EndsWith("Controller", StringComparison.OrdinalIgnoreCase))
        {
            return serviceName.Substring(0, serviceName.Length - 10);
        }
        if (serviceName.EndsWith("Service", StringComparison.OrdinalIgnoreCase))
        {
            return serviceName.Substring(0, serviceName.Length - 7);
        }
        return serviceName;
    }

    public static string ToKebabCase(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var Outcome = Regex.Replace(
            input,
            "([a-z])([A-Z])",
            "$1-$2").ToLowerInvariant();

        return Outcome;
    }

    public static string ToPluralKebabCase(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var kebab = ToKebabCase(input);
        string Outcome;

        if (kebab.EndsWith("y", StringComparison.Ordinal) &&
            !kebab.EndsWith("ay", StringComparison.Ordinal) &&
            !kebab.EndsWith("ey", StringComparison.Ordinal) &&
            !kebab.EndsWith("oy", StringComparison.Ordinal) &&
            !kebab.EndsWith("uy", StringComparison.Ordinal))
        {
            Outcome = string.Concat(
                kebab.AsSpan(
                    0,
                    kebab.Length - 1),
                "ies");
        }
        else if (kebab.EndsWith("s", StringComparison.Ordinal) ||
                 kebab.EndsWith("x", StringComparison.Ordinal) ||
                 kebab.EndsWith("z", StringComparison.Ordinal) ||
                 kebab.EndsWith("ch", StringComparison.Ordinal) ||
                 kebab.EndsWith("sh", StringComparison.Ordinal))
        {
            Outcome = kebab + "es";
        }
        else if (kebab.EndsWith("f", StringComparison.Ordinal))
        {
            Outcome = string.Concat(
                kebab.AsSpan(
                    0,
                    kebab.Length - 1),
                "ves");
        }
        else if (kebab.EndsWith("fe", StringComparison.Ordinal))
        {
            Outcome = string.Concat(
                kebab.AsSpan(
                    0,
                    kebab.Length - 2),
                "ves");
        }
        else
        {
            Outcome = kebab + "s";
        }

        return Outcome;
    }

    public static string ToLowerCamelCase(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var Outcome = char.ToLowerInvariant(input[0]) + input.Substring(1);
        return Outcome;
    }

    private static string ExtractSubResource(string remainder, string resourceName)
    {
        if (remainder.StartsWith(resourceName, StringComparison.OrdinalIgnoreCase))
        {
            remainder = remainder.Substring(resourceName.Length);
        }
        return remainder;
    }
}
