using Atelier.Framework.Primitives;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Atelier.Framework.EventStream.Configuration;
using Atelier.Framework.Infrastructure;
using Atelier.Framework.Attributes;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;
using Microsoft.Extensions.Options;

namespace Atelier.Framework.EventStream;

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class PersistentHashRegistry : HashRegistryBase, IAtelier
{
    [Requisite] private readonly IOptions<EventStreamOptions> _options = null!;

    private readonly ConcurrentDictionary<string, Task> _persistChains = new();

    [Operation("InitializeAsync")]
    public async Task<Outcome> InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome.Failure();
        }

        var directory = Directory;
        System.IO.Directory.CreateDirectory(directory);

        var restored = new List<HashSnapshotEntry>();
        var skipped = 0;
        var corrupt = 0;

        foreach (var file in System.IO.Directory.EnumerateFiles(directory, "*.blob"))
        {
            var hash = Path.GetFileNameWithoutExtension(file);

            byte[] bytes;

            try
            {
                bytes = await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped++;
                Observe(LogLevel.Error, ex, values: [("File", file), ("Hash", hash)]);
                continue;
            }

            if (bytes.Length < sizeof(int))
            {
                skipped++;
                continue;
            }

            var refCount = BitConverter.ToInt32(bytes, 0);
            var blob = bytes[sizeof(int)..];

            var digest = Convert.ToHexStringLower(SHA256.HashData(blob));
            if (!string.Equals(digest, hash, StringComparison.OrdinalIgnoreCase))
            {
                corrupt++;
                Observe(LogLevel.Error, values: [("File", file), ("FileHash", hash), ("ComputedDigest", digest), ("Reason", "DIGEST_MISMATCH")]);
                continue;
            }

            restored.Add(new HashSnapshotEntry(hash, blob, refCount, refCount));
        }

        _store.Restore(restored);

        Observe(LogLevel.Information, values: [("Directory", directory), ("MaxCapacity", _store.MaxCacheSize), ("RestoredCount", restored.Count), ("SkippedCount", skipped), ("CorruptCount", corrupt)]);

        return Outcome.Success();
    }

    [Operation("GarbageCollectAsync")]
    public async Task<Outcome<int>> GarbageCollectAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome<int>.Failure();
        }

        var deleted = _store.GarbageCollect();

        var chains = new List<Task>(deleted.Count);
        foreach (var hash in deleted)
        {
            chains.Add(ChainPersistAsync(hash, cancellationToken));
        }

        await Task.WhenAll(chains).ConfigureAwait(false);

        Observe(LogLevel.Information, values: [("DeletedCount", deleted.Count)]);

        return Outcome<int>.Success(deleted.Count);
    }

    protected override Task OnRegisteredAsync(string hash, byte[] blob, int refCount, CancellationToken cancellationToken)
    {
        return ChainPersistAsync(hash, cancellationToken);
    }

    protected override Task OnReleasedAsync(string hash, CancellationToken cancellationToken)
    {
        return ChainPersistAsync(hash, cancellationToken);
    }

    protected override Task OnEvictedAsync(IReadOnlyList<string> hashes, CancellationToken cancellationToken)
    {
        var chains = new List<Task>(hashes.Count);
        foreach (var hash in hashes)
        {
            chains.Add(ChainPersistAsync(hash, cancellationToken));
        }

        return Task.WhenAll(chains);
    }

    private Task ChainPersistAsync(string hash, CancellationToken cancellationToken)
    {
        while (true)
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var hasPrevious = _persistChains.TryGetValue(hash, out var previous);

            var next = ChainWriteAsync(gate.Task, previous, hash, cancellationToken);

            if (hasPrevious)
            {
                if (_persistChains.TryUpdate(hash, next, previous!))
                {
                    gate.SetResult();
                    return next;
                }
            }
            else if (_persistChains.TryAdd(hash, next))
            {
                gate.SetResult();
                return next;
            }
        }
    }

    private async Task ChainWriteAsync(
        Task gate,
        Task? previous,
        string hash,
        CancellationToken cancellationToken)
    {
        await gate.ConfigureAwait(false);

        if (previous is not null)
        {
            try
            {
                await previous.ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }

        await PersistCurrentStateAsync(hash, cancellationToken).ConfigureAwait(false);
    }

    private Task PersistCurrentStateAsync(string hash, CancellationToken cancellationToken)
    {
        if (!_store.TryGetEntry(hash, out var blob, out var refCount))
        {
            DeleteBlob(hash);
            return Task.CompletedTask;
        }

        var directory = Directory;
        System.IO.Directory.CreateDirectory(directory);

        var payload = new byte[sizeof(int) + blob.Length];
        BitConverter.GetBytes(refCount).CopyTo(payload, 0);
        blob.CopyTo(payload, sizeof(int));

        var path = BlobPath(hash);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";

        return WritePayloadAsync(tempPath, path, payload, hash, cancellationToken);
    }

    private async Task WritePayloadAsync(string tempPath, string path, byte[] payload, string hash, CancellationToken cancellationToken)
    {
        try
        {
            var stream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough);

            await using (stream.ConfigureAwait(false))
            {
                await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Observe(LogLevel.Error, ex, values: [("Hash", hash), ("Path", path)]);

            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                Observe(LogLevel.Error, cleanup, values: [("Hash", hash), ("TempPath", tempPath)]);
            }

            throw;
        }
    }

    private void DeleteBlob(string hash)
    {
        var path = BlobPath(hash);

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Observe(LogLevel.Error, ex, values: [("Hash", hash), ("Path", path)]);
        }
    }

    private string Directory => _options.Value.HashRegistryDirectory;

    private string BlobPath(string hash)
    {
        return Path.Combine(Directory, $"{hash}.blob");
    }
}
