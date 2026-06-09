using Atelier.Framework.Primitives;
using System.Security.Cryptography;
using System.Text;
using Atelier.Framework.Context;
using Atelier.Framework.Context.Extensions;
using Atelier.Framework.Attributes;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;
using Atelier.Framework.Resilience;
using StackExchange.Redis;

namespace Atelier.Facilities.Cache.Redis;

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class RedisCache : ICache, IAtelier
{
    [Requisite] private readonly IRedisConnectionProvider _connection = null!;
    [Requisite] private readonly IContextAccessor _contextAccessor = null!;
    [Requisite] private readonly ResiliencePipelineFactory _resilience = null!;

    public async Task<Outcome<CacheLookup>> GetAsync(
        CacheKey key,
        CancellationToken cancellationToken = default)
    {
        if (!Guard(key, cancellationToken))
        {
            return Outcome<CacheLookup>.Failure();
        }

        var keyHash = HashKey(key.Key);

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "CacheKey", keyHash);

        if (!TryScopedKey(key, "Get", keyHash, out var scopedKey))
        {
            return Outcome<CacheLookup>.Failure();
        }

        var lookup = await _resilience.ExecuteWithResilienceAsync(
            _resilience.RedisPipeline,
            ct => MapTransient("Get", keyHash, () => _connection.StringGetAsync(scopedKey, ct)),
            "Redis.Get",
            cancellationToken).ConfigureAwait(false);

        if (!lookup.IsSuccess)
        {
            return Outcome<CacheLookup>.Failure();
        }

        var stored = lookup.Data;

        if (stored.Value is null)
        {
            if (Logger?.IsEnabled(LogLevel.Debug) == true)
            {
                Observe(LogLevel.Debug, values: [("Operation", "Get"), ("KeyHash", keyHash), ("Hit", false)]);
            }
            return Outcome<CacheLookup>.Success(new CacheLookup
            {
                Found = false
            });
        }

        if (Logger?.IsEnabled(LogLevel.Debug) == true)
        {
            Observe(LogLevel.Debug, values: [("Operation", "Get"), ("KeyHash", keyHash), ("Hit", true)]);
        }

        return Outcome<CacheLookup>.Success(new CacheLookup
        {
            Found = true,
            Value = new CacheValue
            {
                Value = stored.Value,
                Ttl = stored.Ttl
            }
        });
    }

    public async Task<Outcome> SetAsync(
        CacheKey key,
        CacheValue value,
        CancellationToken cancellationToken = default)
    {
        if (!Guard(key, cancellationToken))
        {
            return Outcome.Failure();
        }

        var keyHash = HashKey(key.Key);

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "CacheKey", keyHash);

        if (value is null)
        {
            Observe(LogLevel.Warning, values: [("Operation", "Set"), ("KeyHash", keyHash), ("Reason", "Value cannot be null")]);
            return Outcome.Failure();
        }

        if (value.Value is null)
        {
            Observe(LogLevel.Warning, values: [("Operation", "Set"), ("KeyHash", keyHash), ("Reason", "Value content cannot be null")]);
            return Outcome.Failure();
        }

        if (!TryScopedKey(key, "Set", keyHash, out var scopedKey))
        {
            return Outcome.Failure();
        }

        var write = await _resilience.ExecuteWithResilienceAsync(
            _resilience.RedisPipeline,
            ct => MapTransient("Set", keyHash, () => _connection.StringSetAsync(scopedKey, value.Value, value.Ttl, ct)),
            "Redis.Set",
            cancellationToken).ConfigureAwait(false);

        if (!write.IsSuccess)
        {
            return Outcome.Failure();
        }

        if (!write.Data)
        {
            Observe(LogLevel.Warning, values: [("Operation", "Set"), ("KeyHash", keyHash), ("Reason", "Redis reported the write did not store the entry")]);
            return Outcome.Failure();
        }

        if (Logger?.IsEnabled(LogLevel.Debug) == true)
        {
            Observe(LogLevel.Debug, values: [("Operation", "Set"), ("KeyHash", keyHash)]);
        }

        return Outcome.Success();
    }

    public async Task<Outcome> RemoveAsync(
        CacheKey key,
        CancellationToken cancellationToken = default)
    {
        if (!Guard(key, cancellationToken))
        {
            return Outcome.Failure();
        }

        var keyHash = HashKey(key.Key);

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "CacheKey", keyHash);

        if (!TryScopedKey(key, "Remove", keyHash, out var scopedKey))
        {
            return Outcome.Failure();
        }

        var removal = await _resilience.ExecuteWithResilienceAsync(
            _resilience.RedisPipeline,
            ct => MapTransient("Remove", keyHash, () => _connection.KeyDeleteAsync(scopedKey, ct)),
            "Redis.Remove",
            cancellationToken).ConfigureAwait(false);

        if (!removal.IsSuccess)
        {
            return Outcome.Failure();
        }

        if (Logger?.IsEnabled(LogLevel.Debug) == true)
        {
            Observe(LogLevel.Debug, values: [("Operation", "Remove"), ("KeyHash", keyHash), ("Deleted", removal.Data)]);
        }

        return Outcome.Success();
    }

    private bool TryScopedKey(
        CacheKey key,
        string operation,
        string keyHash,
        out string scopedKey)
    {
        return TenantScope.TryScopedKey(_contextAccessor,
                                        key,
                                        operation,
                                        keyHash,
                                        this,
                                        out scopedKey);
    }

    private static string HashKey(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hash);
    }

    private async Task<Outcome<T>> MapTransient<T>(
        string operation,
        string keyHash,
        Func<Task<T>> call)
    {
        try
        {
            return Outcome<T>.Success(await call().ConfigureAwait(false));
        }
        catch (RedisTimeoutException ex)
        {
            Observe(LogLevel.Warning, ex, values: [("Operation", operation), ("KeyHash", keyHash), ("Reason", "Redis operation timed out")]);
            return Outcome<T>.Failure();
        }
        catch (RedisConnectionException ex)
        {
            Observe(LogLevel.Warning, ex, values: [("Operation", operation), ("KeyHash", keyHash), ("Reason", "Redis connection failed")]);
            return Outcome<T>.Failure();
        }
        catch (RedisException ex)
        {
            Observe(LogLevel.Warning, ex, values: [("Operation", operation), ("KeyHash", keyHash), ("Reason", "Redis operation failed")]);
            return Outcome<T>.Failure();
        }
        catch (ObjectDisposedException ex)
        {
            Observe(LogLevel.Warning, ex, values: [("Operation", operation), ("KeyHash", keyHash), ("Reason", "Redis connection has been disposed")]);
            return Outcome<T>.Failure();
        }
        catch (InvalidOperationException ex)
        {
            Observe(LogLevel.Warning, ex, values: [("Operation", operation), ("KeyHash", keyHash), ("Reason", "Redis connection is not available")]);
            return Outcome<T>.Failure();
        }
    }

    private bool Guard(
        CacheKey key,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            Observe(LogLevel.Warning, values: [("Reason", "Operation was cancelled")]);
            return false;
        }

        if (key is null)
        {
            Observe(LogLevel.Warning, values: [("Reason", "Key cannot be null")]);
            return false;
        }

        if (string.IsNullOrWhiteSpace(key.Key))
        {
            Observe(LogLevel.Warning, values: [("Reason", "Key content cannot be empty")]);
            return false;
        }

        return true;
    }
}
