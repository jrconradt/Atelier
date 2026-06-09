using System.Collections.Concurrent;
using System.Security.Cryptography;
using Atelier.Framework.Outcomes;

namespace Atelier.Framework.EventStream;

public enum HashRegisterStatus
{
    Registered,
    HashMismatch,
    CapacityExceeded
}

public readonly record struct HashRegisterResult(
    HashRegisterStatus Status,
    string ComputedHash,
    int RefCount,
    int TotalHashes);

public readonly record struct HashReleaseResult(
    bool Removed,
    bool Conflicted,
    int Attempts,
    int RefCount,
    bool Underflowed = false);

public readonly record struct HashEvictionResult(
    int EvictedCount,
    int StoreSize,
    bool HeldDominatedOverrun,
    IReadOnlyList<string> EvictedHashes);

public readonly record struct HashSnapshotEntry(
    string Hash,
    byte[] Blob,
    int RefCount,
    long AccessCount);

public sealed class HashRegistryStore
{
    private const int DEFAULT_MAX_CACHE_SIZE = 100000;
    private const int EVICTION_SAMPLE_FACTOR = 5;
    private const int MAX_RELEASE_ATTEMPTS = 64;
    private const int EVICTION_HYSTERESIS_PERCENT = 110;

    private readonly ConcurrentDictionary<string, (byte[] Blob, int RefCount, DateTime LastAccessed, long AccessCount)> _store = new();
    private readonly int _maxCacheSize;

    public HashRegistryStore(int maxCacheSize = DEFAULT_MAX_CACHE_SIZE)
    {
        _maxCacheSize = maxCacheSize;
    }

    public int MaxCacheSize => _maxCacheSize;

    public int Count => _store.Count;

    public HashRegisterResult Register(string hash, byte[] blob)
    {
        var computed = ComputeDigest(blob);
        if (!HashesEqual(computed, hash))
        {
            return new HashRegisterResult(
                HashRegisterStatus.HashMismatch,
                computed,
                0,
                _store.Count);
        }

        if (_store.Count >= _maxCacheSize
            && !_store.ContainsKey(hash)
            && !HasEvictableEntry())
        {
            return new HashRegisterResult(
                HashRegisterStatus.CapacityExceeded,
                computed,
                0,
                _store.Count);
        }

        var entry = _store.AddOrUpdate(
            hash,
            _ => (blob, 1, DateTime.UtcNow, 1),
            (_, existing) => (existing.Blob, existing.RefCount + 1, DateTime.UtcNow, existing.AccessCount + 1));

        return new HashRegisterResult(
            HashRegisterStatus.Registered,
            computed,
            entry.RefCount,
            _store.Count);
    }

    public bool TryTouch(string hash, out byte[]? blob)
    {
        while (_store.TryGetValue(hash, out var entry))
        {
            var updated = (entry.Blob, entry.RefCount, DateTime.UtcNow, entry.AccessCount + 1);
            if (_store.TryUpdate(hash, updated, entry))
            {
                blob = entry.Blob;
                return true;
            }
        }

        blob = null;
        return false;
    }

    public bool Contains(string hash)
    {
        return _store.ContainsKey(hash);
    }

    public int GetReferenceCount(string hash)
    {
        return _store.TryGetValue(hash, out var entry) ? entry.RefCount : 0;
    }

    public bool TryGetEntry(string hash, out byte[] blob, out int refCount)
    {
        if (_store.TryGetValue(hash, out var entry))
        {
            blob = entry.Blob;
            refCount = entry.RefCount;
            return true;
        }

        blob = Array.Empty<byte>();
        refCount = 0;
        return false;
    }

    public HashReleaseResult Release(string hash)
    {
        var attempts = 0;
        while (attempts < MAX_RELEASE_ATTEMPTS
            && _store.TryGetValue(hash, out var entry))
        {
            attempts++;
            if (entry.RefCount <= 0)
            {
                if (_store.TryRemove(new KeyValuePair<string, (byte[], int, DateTime, long)>(hash, entry)))
                {
                    return new HashReleaseResult(false, false, attempts, 0, true);
                }

                continue;
            }

            var nextRefCount = entry.RefCount - 1;
            if (nextRefCount == 0)
            {
                if (_store.TryRemove(new KeyValuePair<string, (byte[], int, DateTime, long)>(hash, entry)))
                {
                    return new HashReleaseResult(true, false, attempts, 0);
                }
            }
            else
            {
                var updated = (entry.Blob, nextRefCount, DateTime.UtcNow, entry.AccessCount);
                if (_store.TryUpdate(hash, updated, entry))
                {
                    return new HashReleaseResult(false, false, attempts, nextRefCount);
                }
            }
        }

        if (attempts >= MAX_RELEASE_ATTEMPTS)
        {
            return new HashReleaseResult(false, true, attempts, 0);
        }

        return new HashReleaseResult(false, false, attempts, 0);
    }

    public IReadOnlyList<HashSnapshotEntry> Snapshot()
    {
        var entries = new List<HashSnapshotEntry>(_store.Count);
        foreach (var kvp in _store)
        {
            entries.Add(new HashSnapshotEntry(kvp.Key, kvp.Value.Blob, kvp.Value.RefCount, kvp.Value.AccessCount));
        }

        return entries;
    }

    public void Restore(IReadOnlyList<HashSnapshotEntry> entries)
    {
        foreach (var entry in entries)
        {
            _store[entry.Hash] = (entry.Blob, entry.RefCount, DateTime.UtcNow, entry.AccessCount);
        }
    }

    public IReadOnlyList<string> GarbageCollect()
    {
        var deleted = new List<string>();
        foreach (var kvp in _store)
        {
            if (kvp.Value.RefCount == 0
                && _store.TryRemove(kvp))
            {
                deleted.Add(kvp.Key);
            }
        }

        return deleted;
    }

    public bool TryEvict(out HashEvictionResult result)
    {
        var highWater = (long)_maxCacheSize * EVICTION_HYSTERESIS_PERCENT / 100;

        if (_store.Count <= highWater)
        {
            result = new HashEvictionResult(0, _store.Count, false, Array.Empty<string>());
            return false;
        }

        var evictCount = _store.Count - _maxCacheSize;
        var sampleSize = Math.Max(evictCount * EVICTION_SAMPLE_FACTOR, _maxCacheSize / 10);
        var candidates = ReservoirSampleEvictable(sampleSize);

        var evictedHashes = new List<string>();
        foreach (var kvp in candidates.OrderBy(kvp => kvp.Value.LastAccessed).Take(evictCount))
        {
            if (_store.TryRemove(kvp))
            {
                evictedHashes.Add(kvp.Key);
            }
        }

        var heldDominatedOverrun = candidates.Count == 0
            && _store.Count > _maxCacheSize;

        result = new HashEvictionResult(evictedHashes.Count, _store.Count, heldDominatedOverrun, evictedHashes);
        return true;
    }

    private List<KeyValuePair<string, (byte[] Blob, int RefCount, DateTime LastAccessed, long AccessCount)>> ReservoirSampleEvictable(int sampleSize)
    {
        var reservoir = new List<KeyValuePair<string, (byte[] Blob, int RefCount, DateTime LastAccessed, long AccessCount)>>(sampleSize);
        var rng = Random.Shared;
        var seen = 0;

        foreach (var kvp in _store)
        {
            if (kvp.Value.RefCount != 0)
            {
                continue;
            }

            if (reservoir.Count < sampleSize)
            {
                reservoir.Add(kvp);
            }
            else
            {
                var index = rng.Next(seen + 1);
                if (index < sampleSize)
                {
                    reservoir[index] = kvp;
                }
            }

            seen++;
        }

        return reservoir;
    }

    private bool HasEvictableEntry()
    {
        foreach (var kvp in _store)
        {
            if (kvp.Value.RefCount == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string ComputeDigest(byte[] blob)
    {
        var digest = SHA256.HashData(blob);
        return Convert.ToHexStringLower(digest);
    }

    private static bool HashesEqual(string computed, string supplied)
    {
        return string.Equals(computed, supplied, StringComparison.OrdinalIgnoreCase);
    }
}
