using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;

namespace Atelier.Framework.EventStream;

public abstract partial class HashRegistryBase : IAtelier, IHashRegistry
{
    protected readonly HashRegistryStore _store = new();

    protected HashRegistryBase()
    {
    }

    public async Task<Outcome<string>> RegisterAsync(
        string hash,
        byte[] blob,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", "Hash was null or empty")]);
            return Outcome<string>.Failure();
        }

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "Hash", hash);

        if (blob == null || blob.Length == 0)
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", "Blob was null or empty"), ("Hash", hash)]);
            return Outcome<string>.Failure();
        }

        var result = _store.Register(hash, blob);
        if (result.Status == HashRegisterStatus.HashMismatch)
        {
            Observe(LogLevel.Warning, values: [("Reason", "Supplied hash does not match digest of blob"), ("SuppliedHash", hash), ("ComputedHash", result.ComputedHash), ("BlobSize", blob.Length)]);

            return Outcome<string>.Failure();
        }

        if (result.Status == HashRegisterStatus.CapacityExceeded)
        {
            Observe(LogLevel.Error, values: [("Reason", "Hash registry is at capacity with no evictable entries; registration rejected"), ("Hash", hash), ("StoreSize", result.TotalHashes), ("MaxCacheSize", _store.MaxCacheSize), ("BlobSize", blob.Length)]);

            return Outcome<string>.Failure();
        }

        Observe(LogLevel.Information, values: [("Hash", hash), ("ReferenceCount", result.RefCount), ("BlobSize", blob.Length), ("TotalHashes", result.TotalHashes)]);

        if (_store.TryEvict(out var eviction))
        {
            Observe(eviction.HeldDominatedOverrun ? LogLevel.Warning : LogLevel.Information, values: [("EvictedCount", eviction.EvictedCount), ("StoreSize", eviction.StoreSize), ("MaxCacheSize", _store.MaxCacheSize), ("HeldDominatedOverrun", eviction.HeldDominatedOverrun)]);

            await OnEvictedAsync(eviction.EvictedHashes, cancellationToken).ConfigureAwait(false);
        }

        await OnRegisteredAsync(hash, blob, result.RefCount, cancellationToken).ConfigureAwait(false);

        return Outcome<string>.Success(hash);
    }

    public Task<Outcome<byte[]?>> LookupAsync(
        string hash,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", "Hash was null or empty")]);
            return Task.FromResult(Outcome<byte[]?>.Failure());
        }

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "Hash", hash);

        if (_store.TryTouch(hash, out var blob))
        {
            Observe(LogLevel.Information, values: [("Hash", hash), ("BlobSize", blob!.Length)]);

            return Task.FromResult(Outcome<byte[]?>.Success(blob));
        }

        Observe(LogLevel.Information, values: [("Hash", hash)]);

        return Task.FromResult(Outcome<byte[]?>.Success(null));
    }

    public Task<Outcome<bool>> ExistsAsync(
        string hash,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", "Hash was null or empty")]);
            return Task.FromResult(Outcome<bool>.Failure());
        }

        return Task.FromResult(Outcome<bool>.Success(_store.Contains(hash)));
    }

    public Task<Outcome<Dictionary<string, byte[]>>> LookupBatchAsync(
        List<string> hashes,
        CancellationToken cancellationToken = default)
    {
        if (hashes == null || hashes.Count == 0)
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", "Hash list was null or empty")]);
            return Task.FromResult(Outcome<Dictionary<string, byte[]>>.Failure());
        }

        var result = new Dictionary<string, byte[]>();

        foreach (var hash in hashes)
        {
            if (!string.IsNullOrWhiteSpace(hash)
                && _store.TryTouch(hash, out var blob))
            {
                result[hash] = blob!;
            }
        }

        Observe(LogLevel.Information, values: [("Requested", hashes.Count), ("Found", result.Count), ("Missing", hashes.Count - result.Count)]);

        return Task.FromResult(Outcome<Dictionary<string, byte[]>>.Success(result));
    }

    public Task<Outcome<int>> GetReferenceCountAsync(
        string hash,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", "Hash was null or empty")]);
            return Task.FromResult(Outcome<int>.Failure());
        }

        return Task.FromResult(Outcome<int>.Success(_store.GetReferenceCount(hash)));
    }

    public async Task<Outcome<int>> ReleaseAsync(
        string hash,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", "Hash was null or empty")]);
            return Outcome<int>.Failure();
        }

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "Hash", hash);

        var result = _store.Release(hash);

        if (result.Conflicted)
        {
            Observe(LogLevel.Warning, values: [("Reason", "Release lost too many concurrent updates"), ("Hash", hash), ("Attempts", result.Attempts)]);

            return Outcome<int>.Failure();
        }

        if (result.Underflowed)
        {
            Observe(LogLevel.Warning, values: [("Reason", "Release underflowed reference count"), ("Hash", hash), ("Attempts", result.Attempts)]);

            return Outcome<int>.Failure();
        }

        Observe(LogLevel.Information, values: [("Hash", hash), ("ReferenceCount", result.RefCount), ("Removed", result.Removed)]);

        if (result.Removed)
        {
            await OnReleasedAsync(hash, cancellationToken).ConfigureAwait(false);
        }

        return Outcome<int>.Success(result.RefCount);
    }

    protected virtual Task OnRegisteredAsync(string hash, byte[] blob, int refCount, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    protected virtual Task OnReleasedAsync(string hash, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    protected virtual Task OnEvictedAsync(IReadOnlyList<string> hashes, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
