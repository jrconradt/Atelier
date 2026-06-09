using System.Text.RegularExpressions;

namespace Atelier.Framework.Infrastructure.Generators;

public static class NamingConventions
{
    public static readonly string[] READER_PREFIXES =
    {
        "get",
        "fetch",
        "retrieve",
        "discover",
        "find",
        "list",
        "query",
        "search"
    };

    public static readonly string[] MUTATOR_TOKENS =
    {
        "create",
        "add",
        "insert",
        "register",
        "publish",
        "submit",
        "send",
        "post",
        "start",
        "begin",
        "invoke",
        "execute",
        "handle",
        "update",
        "modify",
        "edit",
        "replace",
        "set",
        "delete",
        "remove",
        "unregister",
        "release",
        "revoke",
        "stop",
        "cancel",
        "patch",
        "purge",
        "archive",
        "reset",
        "provision",
        "furnish"
    };

    public static readonly string[] CONJUNCTION_TOKENS =
    {
        "or",
        "and",
        "then"
    };

    public static bool IsReaderMethod(string methodName)
    {
        var stripped = methodName.EndsWith("Async") ? methodName.Substring(0, methodName.Length - 5) : methodName;
        if (string.IsNullOrWhiteSpace(stripped))
        {
            return false;
        }

        var methodLower = stripped.ToLowerInvariant();

        var matchedPrefix = string.Empty;
        foreach (var prefix in READER_PREFIXES)
        {
            if (methodLower.StartsWith(prefix))
            {
                matchedPrefix = prefix;
                break;
            }
        }

        if (matchedPrefix.Length == 0)
        {
            return false;
        }

        if (IsLexicallyAmbiguous(methodLower, matchedPrefix))
        {
            return false;
        }

        return true;
    }

    public static bool IsLexicallyAmbiguous(string loweredStrippedName,
                                            string matchedReaderPrefix)
    {
        var remainder = loweredStrippedName.Substring(matchedReaderPrefix.Length);

        foreach (var token in MUTATOR_TOKENS)
        {
            if (remainder.Contains(token))
            {
                return true;
            }
        }

        foreach (var conjunction in CONJUNCTION_TOKENS)
        {
            if (remainder.StartsWith(conjunction)
                && remainder.Length > conjunction.Length)
            {
                return true;
            }
        }

        return false;
    }

    public static string ExtractResourceName(string className)
    {
        var resourceName = Regex.Replace(className, @"(Controller|Service|Repository|Manager|Handler|Provider)$", string.Empty, RegexOptions.IgnoreCase);
        return resourceName;
    }

    public static string ToPluralKebabCase(string resourceName)
    {
        var kebabCase = ToKebabCase(resourceName);
        return MakePlural(kebabCase);
    }

    public static (string HttpMethod, string RoutePattern, bool HasIdParameter) InferEndpointDetails(string methodName, string resourceName)
    {

        var stripped = methodName.EndsWith("Async") ? methodName.Substring(0, methodName.Length - 5) : methodName;
        var methodLower = stripped.ToLowerInvariant();

        if (IsReaderMethod(stripped))
        {
            if (methodLower.Contains("by") && methodLower.Contains("id"))
            {
                return ("Get", "/{id}", true);
            }
            if (methodLower.Contains("all") || methodLower.EndsWith("s") || methodLower.EndsWith("list"))
            {
                return ("Get", string.Empty, false);
            }
            return ("Get", "/{id}", true);
        }

        if (methodLower.StartsWith("create") || methodLower.StartsWith("add") || methodLower.StartsWith("insert") ||
            methodLower.StartsWith("register") || methodLower.StartsWith("publish") || methodLower.StartsWith("submit") ||
            methodLower.StartsWith("send") || methodLower.StartsWith("post") || methodLower.StartsWith("requisition") ||
            methodLower.StartsWith("furnish") || methodLower.StartsWith("start") || methodLower.StartsWith("begin") ||
            methodLower.StartsWith("invoke") || methodLower.StartsWith("execute") || methodLower.StartsWith("handle"))
        {
            return ("Post", string.Empty, false);
        }

        if (methodLower.StartsWith("update") || methodLower.StartsWith("modify") || methodLower.StartsWith("edit") ||
            methodLower.StartsWith("replace") || methodLower.StartsWith("set"))
        {
            return ("Put", "/{id}", true);
        }

        if (methodLower.StartsWith("delete") || methodLower.StartsWith("remove") || methodLower.StartsWith("unregister") ||
            methodLower.StartsWith("release") || methodLower.StartsWith("revoke") || methodLower.StartsWith("stop") ||
            methodLower.StartsWith("cancel"))
        {
            return ("Delete", "/{id}", true);
        }

        if (methodLower.StartsWith("patch") || methodLower.Contains("partial"))
        {
            return ("Patch", "/{id}", true);
        }


        return ("Post", $"/{ToKebabCase(stripped)}", false);
    }

    private static string ToKebabCase(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var kebabCase = Regex.Replace(input, @"(?<!^)([A-Z])", "-$1").ToLowerInvariant();
        return kebabCase;
    }

    private static string MakePlural(string word)
    {
        if (string.IsNullOrEmpty(word))
        {
            return word;
        }

        if (word.EndsWith("s") || word.EndsWith("sh") || word.EndsWith("ch") || word.EndsWith("x") || word.EndsWith("z"))
        {
            return word + "es";
        }
        else if (word.EndsWith("y") && word.Length > 1 && !IsVowel(word[word.Length - 2]))
        {
            return word.Substring(0, word.Length - 1) + "ies";
        }
        else if (word.EndsWith("f"))
        {
            return word.Substring(0, word.Length - 1) + "ves";
        }
        else if (word.EndsWith("fe"))
        {
            return word.Substring(0, word.Length - 2) + "ves";
        }
        else if (word.EndsWith("o") && word.Length > 1 && !IsVowel(word[word.Length - 2]))
        {
            return word + "es";
        }
        else
        {
            return word + "s";
        }
    }

    private static bool IsVowel(char c)
    {
        return "aeiouAEIOU".IndexOf(c) >= 0;
    }
}
