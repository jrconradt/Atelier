using Atelier.Framework.Context;
namespace Atelier.Framework.Context.Extensions
{
    public static class ContextScopeLimiterExtensions
    {
        public static IContext WithScopeLimiter(this IContext context, Action<ScopeLimiterContextExtension> configure)
        {
            var extension = new ScopeLimiterContextExtension();
            configure(extension);
            context.Extensions.Register(extension);
            return context;
        }

        public static ScopeLimiterContextExtension? GetScopeLimiter(this IContext context)
        {
            return context.Extensions.Get<ScopeLimiterContextExtension>();
        }

        public static bool IsDataKeyAllowed(this IContext context, string key)
        {
            var limiter = context.GetScopeLimiter();
            return limiter?.IsDataKeyAllowed(key) ?? true;
        }

        public static bool IsOperationAllowed(this IContext context, string operation)
        {
            var limiter = context.GetScopeLimiter();
            return limiter?.IsOperationAllowed(operation) ?? true;
        }

        public static bool IsScopeAllowed(this IContext context, ContextScope scope)
        {
            var limiter = context.GetScopeLimiter();
            return limiter?.IsScopeAllowed(scope) ?? true;
        }
    }
}
