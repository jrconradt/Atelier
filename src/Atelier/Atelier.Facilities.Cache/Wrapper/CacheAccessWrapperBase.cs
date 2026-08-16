using Atelier.Framework.Outcomes;
using System.Threading;
using System.Threading.Tasks;

namespace Atelier.Facilities.Cache;

public abstract class CacheAccessWrapperBase : ICache
{
    public abstract Task<Outcome<CacheLookup>> GetAsync(
        CacheKey key,
        CancellationToken cancellationToken = default);

    public abstract Task<Outcome> SetAsync(
        CacheKey key,
        CacheValue value,
        CancellationToken cancellationToken = default);

    public abstract Task<Outcome> RemoveAsync(
        CacheKey key,
        CancellationToken cancellationToken = default);
}
