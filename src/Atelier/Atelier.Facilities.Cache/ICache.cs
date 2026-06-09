using Atelier.Framework.Attributes;
using Atelier.Framework.Outcomes;

namespace Atelier.Facilities.Cache;

[Facility("Cache",
          RequiresAuthentication = true,
          AllowAnonymous = false,
          RequiredScopes = new[] { "cache.access" })]
public interface ICache
{
    public Task<Outcome<CacheLookup>> GetAsync(
        CacheKey key,
        CancellationToken cancellationToken = default);

    public Task<Outcome> SetAsync(
        CacheKey key,
        CacheValue value,
        CancellationToken cancellationToken = default);

    public Task<Outcome> RemoveAsync(
        CacheKey key,
        CancellationToken cancellationToken = default);
}
