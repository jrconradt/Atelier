using Atelier.Framework.Context;
using Atelier.Framework.Context.Extensions;
using Atelier.Framework.Observability;

namespace Atelier.Facilities.Cache;

public static class TenantScope
{
    private const string MISSING_TENANT_REASON = "Tenant scope is required and was absent from the current context";

    public static bool TryScopedKey(
        IContextAccessor contextAccessor,
        CacheKey key,
        string operation,
        string keyTag,
        IAtelier observer,
        out string scopedKey)
    {
        var tenant = contextAccessor.Current?.GetTenantId();
        if (string.IsNullOrEmpty(tenant))
        {
            observer.Observe(LogLevel.Warning,
                             values: [("Operation", operation), ("KeyHash", keyTag), ("Reason", MISSING_TENANT_REASON)]);
            scopedKey = string.Empty;
            return false;
        }

        scopedKey = key.Composite(tenant);
        return true;
    }
}
