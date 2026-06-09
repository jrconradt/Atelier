using Atelier.Framework.Context;

namespace Atelier.Framework.Network.Enforcement;

public static class ScopeAuthorizationEvaluator
{
    public static bool IsAuthorized(AuthorizationContext? authorization,
                                    ScopeRequirement requiredScopes)
    {
        if (requiredScopes.FailClosed)
        {
            return false;
        }

        if (requiredScopes.Count == 0)
        {
            return true;
        }

        if (authorization == null
            || !authorization.IsVerified
            || !authorization.IsValid())
        {
            return false;
        }

        foreach (var scope in requiredScopes.Scopes)
        {
            if (!authorization.HasPermission(scope))
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsSelf(AuthorizationContext? authorization,
                              string? identityArgument)
    {
        if (authorization == null
            || !authorization.IsVerified
            || !authorization.IsValid())
        {
            return false;
        }

        if (string.IsNullOrEmpty(authorization.UserId)
            || string.IsNullOrEmpty(identityArgument))
        {
            return false;
        }

        return string.Equals(authorization.UserId, identityArgument, StringComparison.Ordinal);
    }
}
