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
        if (string.IsNullOrEmpty(methodName))
        {
            return true;
        }

        var stripped = methodName.EndsWith("Async") ? methodName.Substring(0, methodName.Length - 5) : methodName;
        if (string.IsNullOrWhiteSpace(stripped))
        {
            return true;
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
            return true;
        }

        if (IsLexicallyAmbiguous(lowered, matchedPrefix))
        {
            return true;
        }

        return false;
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
}
