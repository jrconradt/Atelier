using Atelier.Facilities.Cache;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Resilience;
using Atelier.Framework.Testing;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;
using ILogger = Atelier.Framework.Observability.ILogger;

namespace Atelier.Facilities.Cache.Redis;

public static class RedisCacheBehaviorTests
{
    private static ResiliencePipelineFactory CreateResilience()
    {
        return new ResiliencePipelineFactory(new ConfigurationBuilder().Build(),
                                             AutoMockProvider.For<ILogger>());
    }

    private static RedisCache CreateCache(IRedisConnectionProvider connection)
    {
        return new RedisCache(connection,
                              CreateResilience(),
                              AutoMockProvider.For<ILogger>());
    }

    private static CacheKey Key()
    {
        return new CacheKey
        {
            Key = "user:42",
            Namespace = "sessions"
        };
    }

    [GeneratedTest("Cache/Redis-Composite-Key-Includes-Namespace", "global::Atelier.Facilities.Cache.Redis.RedisCache")]
    public static async Task ScopedKeyIncludesNamespace()
    {
        var connection = new StubConnectionProvider
        {
            StoredValue = "payload"
        };
        var cache = CreateCache(connection);

        var outcome = await cache.GetAsync(Key()).ConfigureAwait(false);

        if (!outcome.IsSuccess)
        {
            throw new InvalidOperationException("Get failed unexpectedly");
        }
        if (connection.LastKey is null)
        {
            throw new InvalidOperationException("Get never reached the connection");
        }
        if (connection.LastKey != "sessions:user:42")
        {
            throw new InvalidOperationException($"composite key '{connection.LastKey}' is not namespace-prefixed correctly");
        }
    }

    [GeneratedTest("Cache/Redis-Get-Miss-Shape", "global::Atelier.Facilities.Cache.Redis.RedisCache")]
    public static async Task GetMissReturnsNotFound()
    {
        var connection = new StubConnectionProvider
        {
            StoredValue = null
        };
        var cache = CreateCache(connection);

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

    [GeneratedTest("Cache/Redis-Get-Hit-Shape", "global::Atelier.Facilities.Cache.Redis.RedisCache")]
    public static async Task GetHitReturnsStoredValue()
    {
        var connection = new StubConnectionProvider
        {
            StoredValue = "stored-payload",
            StoredTtl = TimeSpan.FromMinutes(3)
        };
        var cache = CreateCache(connection);

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
        if (outcome.Data.Value?.Ttl != TimeSpan.FromMinutes(3))
        {
            throw new InvalidOperationException("cache hit lost the stored TTL");
        }
    }

    [GeneratedTest("Cache/Redis-Set-False-Write-Maps-SetFailed", "global::Atelier.Facilities.Cache.Redis.RedisCache")]
    public static async Task SetFalseWriteMapsToSetFailed()
    {
        var connection = new StubConnectionProvider
        {
            SetResult = false
        };
        var cache = CreateCache(connection);

        var outcome = await cache.SetAsync(Key(), new CacheValue { Value = "v" }).ConfigureAwait(false);

        if (outcome.IsSuccess)
        {
            throw new InvalidOperationException("Set succeeded even though the write returned false");
        }
        if (connection.LastValue != "v")
        {
            throw new InvalidOperationException("Set did not attempt the write before failing");
        }
    }

    [GeneratedTest("Cache/Redis-Set-Success", "global::Atelier.Facilities.Cache.Redis.RedisCache")]
    public static async Task SetTrueWriteSucceeds()
    {
        var connection = new StubConnectionProvider
        {
            SetResult = true
        };
        var cache = CreateCache(connection);

        var outcome = await cache.SetAsync(Key(), new CacheValue { Value = "v" }).ConfigureAwait(false);

        if (!outcome.IsSuccess)
        {
            throw new InvalidOperationException("Set failed on a true write");
        }
        if (connection.LastValue != "v")
        {
            throw new InvalidOperationException("Set did not forward the value to the connection");
        }
    }

    [GeneratedTest("Cache/Redis-Timeout-Fails-Closed", "global::Atelier.Facilities.Cache.Redis.RedisCache")]
    public static async Task TimeoutExceptionFailsClosed()
    {
        var connection = new StubConnectionProvider
        {
            ThrowOnGet = () => new RedisTimeoutException("boom", CommandStatus.Unknown)
        };
        var cache = CreateCache(connection);

        var outcome = await cache.GetAsync(Key()).ConfigureAwait(false);

        if (outcome.IsSuccess)
        {
            throw new InvalidOperationException("Get succeeded despite a Redis timeout");
        }
    }

    [GeneratedTest("Cache/Redis-Connection-Failure-Fails-Closed", "global::Atelier.Facilities.Cache.Redis.RedisCache")]
    public static async Task ConnectionExceptionFailsClosed()
    {
        var connection = new StubConnectionProvider
        {
            ThrowOnGet = () => new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down")
        };
        var cache = CreateCache(connection);

        var outcome = await cache.GetAsync(Key()).ConfigureAwait(false);

        if (outcome.IsSuccess)
        {
            throw new InvalidOperationException("Get succeeded despite a Redis connection failure");
        }
    }

    [GeneratedTest("Cache/Redis-Disposed-Fails-Closed", "global::Atelier.Facilities.Cache.Redis.RedisCache")]
    public static async Task DisposedExceptionFailsClosed()
    {
        var connection = new StubConnectionProvider
        {
            ThrowOnGet = () => new ObjectDisposedException("multiplexer")
        };
        var cache = CreateCache(connection);

        var outcome = await cache.GetAsync(Key()).ConfigureAwait(false);

        if (outcome.IsSuccess)
        {
            throw new InvalidOperationException("Get succeeded despite a disposed connection");
        }
    }

    [GeneratedTest("Cache/Redis-Cancelled-Guard", "global::Atelier.Facilities.Cache.Redis.RedisCache")]
    public static async Task CancelledTokenIsGuarded()
    {
        var connection = new StubConnectionProvider();
        var cache = CreateCache(connection);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var outcome = await cache.GetAsync(Key(), cts.Token).ConfigureAwait(false);

        if (outcome.IsSuccess)
        {
            throw new InvalidOperationException("Get succeeded on an already-cancelled token");
        }
        if (connection.LastKey is not null)
        {
            throw new InvalidOperationException("cancelled Get still reached the connection");
        }
    }

    [GeneratedTest("Cache/Redis-Empty-Key-Guard", "global::Atelier.Facilities.Cache.Redis.RedisCache")]
    public static async Task EmptyKeyIsGuarded()
    {
        var connection = new StubConnectionProvider();
        var cache = CreateCache(connection);

        var outcome = await cache.GetAsync(new CacheKey { Key = "   " }).ConfigureAwait(false);

        if (outcome.IsSuccess)
        {
            throw new InvalidOperationException("Get succeeded on a whitespace key");
        }
        if (connection.LastKey is not null)
        {
            throw new InvalidOperationException("whitespace-key Get still reached the connection");
        }
    }

    [GeneratedTest("Cache/Redis-Remove-Success", "global::Atelier.Facilities.Cache.Redis.RedisCache")]
    public static async Task RemoveSucceedsAndScopesKey()
    {
        var connection = new StubConnectionProvider
        {
            DeleteResult = true
        };
        var cache = CreateCache(connection);

        var outcome = await cache.RemoveAsync(Key()).ConfigureAwait(false);

        if (!outcome.IsSuccess)
        {
            throw new InvalidOperationException("Remove failed unexpectedly");
        }
        if (connection.LastKey is null || connection.LastKey != "sessions:user:42")
        {
            throw new InvalidOperationException($"Remove did not construct the correct key: '{connection.LastKey}'");
        }
    }

    private sealed class StubConnectionProvider : IRedisConnectionProvider
    {
        public string? StoredValue { get; init; }
        public TimeSpan? StoredTtl { get; init; }
        public bool SetResult { get; init; } = true;
        public bool DeleteResult { get; init; } = true;
        public Func<Exception>? ThrowOnGet { get; init; }

        public string? LastKey { get; private set; }
        public string? LastValue { get; private set; }

        public bool IsConfigured => true;

        public bool IsConnected => true;

        public Outcome<IRedisConnectionProvider> Configure(string connectionString)
        {
            return Outcome<IRedisConnectionProvider>.Success(this);
        }

        public Task<(string? Value, TimeSpan? Ttl)> StringGetAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            LastKey = key;
            if (ThrowOnGet is not null)
            {
                throw ThrowOnGet();
            }
            return Task.FromResult((StoredValue, StoredTtl));
        }

        public Task<bool> StringSetAsync(string key,
                                         string value,
                                         TimeSpan? expiry,
                                         CancellationToken cancellationToken = default)
        {
            LastKey = key;
            LastValue = value;
            return Task.FromResult(SetResult);
        }

        public Task<bool> KeyDeleteAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            LastKey = key;
            return Task.FromResult(DeleteResult);
        }
    }
}
