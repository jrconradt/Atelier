using Atelier.Framework.Testing;
using ILogger = Atelier.Framework.Observability.ILogger;

using Atelier.Facilities.Cache;
namespace Atelier.Facilities.Cache.InMemory;

public static class InMemoryCacheBehaviorTests
{
    private static InMemoryCache CreateCache()
    {
        return new InMemoryCache(AutoMockProvider.For<ILogger>());
    }

    private static CacheKey Key()
    {
        return new CacheKey
        {
            Key = "user:42",
            Namespace = "sessions"
        };
    }

    [GeneratedTest("Cache/InMemory-Get-Miss-Shape", "global::Atelier.Facilities.Cache.InMemoryCache")]
    public static async Task GetMissReturnsNotFound()
    {
        var cache = CreateCache();

        var outcome = await cache.GetAsync(Key()).ConfigureAwait(false);

        if (!outcome.IsSuccess)
        {
            throw new InvalidOperationException("cache miss surfaced a failure");
        }
        if (outcome.Data.Found)
        {
            throw new InvalidOperationException("cache miss reported Found = true");
        }
        if (outcome.Data.Value is not null)
        {
            throw new InvalidOperationException("cache miss carried a non-null value");
        }
    }

    [GeneratedTest("Cache/InMemory-Set-Then-Get-Roundtrip", "global::Atelier.Facilities.Cache.InMemoryCache")]
    public static async Task SetThenGetReturnsStoredValue()
    {
        var cache = CreateCache();

        var set = await cache.SetAsync(Key(),
                                       new CacheValue
                                       {
                                           Value = "stored-payload",
                                           Ttl = TimeSpan.FromMinutes(3)
                                       }).ConfigureAwait(false);

        if (!set.IsSuccess)
        {
            throw new InvalidOperationException("Set failed unexpectedly");
        }

        var outcome = await cache.GetAsync(Key()).ConfigureAwait(false);

        if (!outcome.IsSuccess)
        {
            throw new InvalidOperationException("cache hit surfaced a failure");
        }
        if (!outcome.Data.Found)
        {
            throw new InvalidOperationException("cache hit reported Found = false");
        }
        if (outcome.Data.Value?.Value != "stored-payload")
        {
            throw new InvalidOperationException($"cache hit returned '{outcome.Data.Value?.Value}' instead of the stored value");
        }
        var remaining = outcome.Data.Value?.Ttl;
        if (remaining is not { } ttl
            || ttl <= TimeSpan.Zero
            || ttl > TimeSpan.FromMinutes(3))
        {
            throw new InvalidOperationException("cache hit lost the remaining TTL");
        }
    }

    [GeneratedTest("Cache/InMemory-Expired-Entry-Reads-As-Miss", "global::Atelier.Facilities.Cache.InMemoryCache")]
    public static async Task ExpiredEntryReadsAsMiss()
    {
        var cache = CreateCache();

        var set = await cache.SetAsync(Key(),
                                       new CacheValue
                                       {
                                           Value = "ephemeral",
                                           Ttl = TimeSpan.FromMilliseconds(-1)
                                       }).ConfigureAwait(false);

        if (!set.IsSuccess)
        {
            throw new InvalidOperationException("Set failed unexpectedly");
        }

        var outcome = await cache.GetAsync(Key()).ConfigureAwait(false);

        if (!outcome.IsSuccess)
        {
            throw new InvalidOperationException("expired read surfaced a failure");
        }
        if (outcome.Data.Found)
        {
            throw new InvalidOperationException("an expired entry was returned as a hit");
        }
    }

    [GeneratedTest("Cache/InMemory-Remove-Then-Get-Misses", "global::Atelier.Facilities.Cache.InMemoryCache")]
    public static async Task RemoveDeletesTheEntry()
    {
        var cache = CreateCache();

        await cache.SetAsync(Key(), new CacheValue { Value = "v" }).ConfigureAwait(false);

        var removed = await cache.RemoveAsync(Key()).ConfigureAwait(false);

        if (!removed.IsSuccess)
        {
            throw new InvalidOperationException("Remove failed unexpectedly");
        }

        var outcome = await cache.GetAsync(Key()).ConfigureAwait(false);

        if (!outcome.IsSuccess)
        {
            throw new InvalidOperationException("post-remove read surfaced a failure");
        }
        if (outcome.Data.Found)
        {
            throw new InvalidOperationException("Remove did not delete the entry");
        }
    }

    [GeneratedTest("Cache/InMemory-Cancelled-Guard", "global::Atelier.Facilities.Cache.InMemoryCache")]
    public static async Task CancelledTokenIsGuarded()
    {
        var cache = CreateCache();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var outcome = await cache.GetAsync(Key(), cts.Token).ConfigureAwait(false);

        if (outcome.IsSuccess)
        {
            throw new InvalidOperationException("Get succeeded on an already-cancelled token");
        }
    }

    [GeneratedTest("Cache/InMemory-Empty-Key-Guard", "global::Atelier.Facilities.Cache.InMemoryCache")]
    public static async Task EmptyKeyIsGuarded()
    {
        var cache = CreateCache();

        var outcome = await cache.GetAsync(new CacheKey { Key = "   " }).ConfigureAwait(false);

        if (outcome.IsSuccess)
        {
            throw new InvalidOperationException("Get succeeded on a whitespace key");
        }
    }
}
