namespace Atelier.Framework.Identity.Authorization;

public static class OperationEffectClassifier
{
    public static readonly string[] READER_PREFIXES = new[]
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

    public static readonly string[] MUTATOR_TOKENS = new[]
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

    public static readonly string[] CONJUNCTION_TOKENS = new[]
    {
        "or",
        "and",
        "then"
    };

    public static bool IsMutatingOperation(string methodName)
    {
        return !IsConfidentReadOperation(methodName);
    }

    private static bool IsConfidentReadOperation(string methodName)
    {
        if (string.IsNullOrEmpty(methodName))
        {
            return false;
        }

        var stripped = methodName.EndsWith("Async") ? methodName.Substring(0, methodName.Length - "Async".Length) : methodName;
        if (string.IsNullOrWhiteSpace(stripped))
        {
            return false;
        }

        var lowered = stripped.ToLowerInvariant();

        var matchedPrefix = string.Empty;
        foreach (var prefix in READER_PREFIXES)
        {
            if (lowered.StartsWith(prefix))
            {
                matchedPrefix = prefix;
                break;
            }
        }

        if (matchedPrefix.Length == 0)
        {
            return false;
        }

        return !RemainderShowsMutationSignal(stripped.Substring(matchedPrefix.Length));
    }

    private static bool RemainderShowsMutationSignal(string remainder)
    {
        foreach (var word in SplitRemainderWords(remainder))
        {
            foreach (var conjunction in CONJUNCTION_TOKENS)
            {
                if (word == conjunction)
                {
                    return true;
                }
            }

            foreach (var token in MUTATOR_TOKENS)
            {
                if (word.StartsWith(token))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static List<string> SplitRemainderWords(string remainder)
    {
        var words = new List<string>();
        var current = new List<char>();
        foreach (var character in remainder)
        {
            if (char.IsUpper(character)
                && current.Count > 0)
            {
                words.Add(new string(current.ToArray()).ToLowerInvariant());
                current = new List<char>();
            }

            current.Add(character);
        }

        if (current.Count > 0)
        {
            words.Add(new string(current.ToArray()).ToLowerInvariant());
        }

        return words;
    }
}
