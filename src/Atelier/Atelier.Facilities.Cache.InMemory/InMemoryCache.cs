using Atelier.Framework.Primitives;
using System.Collections.Concurrent;
using Atelier.Framework.Context;
using Atelier.Framework.Context.Extensions;
using Atelier.Framework.Attributes;
using Atelier.Framework.Infrastructure;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;

using Atelier.Facilities.Cache;
namespace Atelier.Facilities.Cache.InMemory;

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class InMemoryCache : ICache, IAtelier
{
    [Requisite] private readonly IContextAccessor _contextAccessor = null!;

    private readonly ConcurrentDictionary<string, StoredEntry> _entries = new(StringComparer.Ordinal);

    public Task<Outcome<CacheLookup>> GetAsync(
        CacheKey key,
        CancellationToken cancellationToken = default)
    {
        var guard = Guard(key, cancellationToken);
        if (guard is { } guardReason)
        {
            Observe(LogLevel.Warning,
                    values: [("Reason", guardReason), ("Operation", "Get")]);
            return Task.FromResult(Outcome<CacheLookup>.Failure());
        }

        using var __entity = EntityContext.Enter(ContextAccessor, "CacheKey", key.Key);

        if (!TenantScope.TryScopedKey(_contextAccessor,
                                      key,
                                      "Get",
                                      key.Key,
                                      this,
                                      out var scopedKey))
        {
            return Task.FromResult(Outcome<CacheLookup>.Failure());
        }

        if (_entries.TryGetValue(scopedKey, out var stored))
        {
            var now = DateTimeOffset.UtcNow;
            if (stored.ExpiresAt is { } expiresAt
                && expiresAt <= now)
            {
                _entries.TryRemove(new KeyValuePair<string, StoredEntry>(scopedKey, stored));
                return Task.FromResult(Outcome<CacheLookup>.Success(new CacheLookup
                {
                    Found = false
                }));
            }

            var remaining = stored.ExpiresAt is { } expiry ? expiry - now : (TimeSpan?)null;

            return Task.FromResult(Outcome<CacheLookup>.Success(new CacheLookup
            {
                Found = true,
                Value = new CacheValue
                {
                    Value = stored.Value,
                    Ttl = remaining
                }
            }));
        }

        return Task.FromResult(Outcome<CacheLookup>.Success(new CacheLookup
        {
            Found = false
        }));
    }

    public Task<Outcome> SetAsync(
        CacheKey key,
        CacheValue value,
        CancellationToken cancellationToken = default)
    {
        var guard = Guard(key, cancellationToken);
        if (guard is { } guardReason)
        {
            Observe(LogLevel.Warning,
                    values: [("Reason", guardReason), ("Operation", "Set")]);
            return Task.FromResult(Outcome.Failure());
        }

        using var __entity = EntityContext.Enter(ContextAccessor, "CacheKey", key.Key);

        if (value is null)
        {
            Observe(LogLevel.Warning,
                    values: [("Reason", $"{nameof(value)} cannot be null"), ("Operation", "Set"), ("Key", key.Key)]);
            return Task.FromResult(Outcome.Failure());
        }

        if (value.Value is null)
        {
            Observe(LogLevel.Warning,
                    values: [("Reason", $"{nameof(value)}.{nameof(value.Value)} cannot be null"), ("Operation", "Set"), ("Key", key.Key)]);
            return Task.FromResult(Outcome.Failure());
        }

        if (!TenantScope.TryScopedKey(_contextAccessor,
                                      key,
                                      "Set",
                                      key.Key,
                                      this,
                                      out var scopedKey))
        {
            return Task.FromResult(Outcome.Failure());
        }

        var expiresAt = value.Ttl is { } ttl ? DateTimeOffset.UtcNow + ttl : (DateTimeOffset?)null;
        _entries[scopedKey] = new StoredEntry(value.Value, expiresAt);

        return Task.FromResult(Outcome.Success());
    }

    public Task<Outcome> RemoveAsync(
        CacheKey key,
        CancellationToken cancellationToken = default)
    {
        var guard = Guard(key, cancellationToken);
        if (guard is { } guardReason)
        {
            Observe(LogLevel.Warning,
                    values: [("Reason", guardReason), ("Operation", "Remove")]);
            return Task.FromResult(Outcome.Failure());
        }

        using var __entity = EntityContext.Enter(ContextAccessor, "CacheKey", key.Key);

        if (!TenantScope.TryScopedKey(_contextAccessor,
                                      key,
                                      "Remove",
                                      key.Key,
                                      this,
                                      out var scopedKey))
        {
            return Task.FromResult(Outcome.Failure());
        }

        _entries.TryRemove(scopedKey, out _);

        return Task.FromResult(Outcome.Success());
    }

    private static string? Guard(
        CacheKey key,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return "Operation was cancelled";
        }

        if (key is null)
        {
            return $"{nameof(key)} cannot be null";
        }

        if (string.IsNullOrWhiteSpace(key.Key))
        {
            return $"{nameof(key)}.{nameof(key.Key)} cannot be empty";
        }

        return null;
    }

    private readonly record struct StoredEntry(string Value,
                                               DateTimeOffset? ExpiresAt);
}
