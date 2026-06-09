using Atelier.Framework.Context;
namespace Atelier.Framework.Context.Extensions
{
    public static class ContextAuthorizationExtensions
    {
        public static AuthorizationContext? GetAuthorization(this IContext context)
        {
            return context.Authorization;
        }

        public static bool IsAuthorized(this IContext context, string permission)
        {
            var auth = context.Authorization;
            return auth != null
                && auth.IsVerified
                && auth.IsValid()
                && auth.HasPermission(permission);
        }

        public static bool IsAuthorizedForRole(this IContext context, string role)
        {
            var auth = context.Authorization;
            return auth != null
                && auth.IsVerified
                && auth.IsValid()
                && auth.HasRole(role);
        }

        public static string? GetUserId(this IContext context)
        {
            return context.Authorization?.UserId;
        }

        public static string? GetTenantId(this IContext context)
        {
            return context.Authorization?.TenantId;
        }

        public static string? GetSessionId(this IContext context)
        {
            return context.Authorization?.SessionId;
        }
    }
}
