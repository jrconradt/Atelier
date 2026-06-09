
namespace Atelier.Framework.Context.Extensions;

public static class ContextAccessorExtensions
{
    public static IContext GetCurrentContext(this IContextAccessor accessor)
    {
        return accessor.Current
            ?? throw new InvalidOperationException("No context available. Ensure a valid context is available before accessing the Context property.");
    }

    public static string? GetCurrentUserId(this IContextAccessor accessor)
    {
        return accessor.GetCurrentContext().Authorization?.UserId;
    }

    public static string? GetCurrentTenantId(this IContextAccessor accessor)
    {
        return accessor.GetCurrentContext().Authorization?.TenantId;
    }

    public static string? GetCurrentSessionId(this IContextAccessor accessor)
    {
        return accessor.GetCurrentContext().Authorization?.SessionId;
    }
}
