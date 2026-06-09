using System.Security.Cryptography;
using Atelier.Framework.EventStream.Configuration;
using Atelier.Framework.Testing;
using Microsoft.Extensions.Options;

namespace Atelier.Framework.EventStream;

public static class PersistentHashRegistryBehaviorTests
{
    private static string DigestOf(byte[] blob)
    {
        return Convert.ToHexStringLower(SHA256.HashData(blob));
    }

    private static (PersistentHashRegistry Registry, string Directory) NewRegistry()
    {
        var directory = Path.Combine(Path.GetTempPath(), "atelier-test", "hashes", Guid.NewGuid().ToString("N"));
        var options = Options.Create(new EventStreamOptions
        {
            HashRegistryDirectory = directory
        });
        return (new PersistentHashRegistry(options, null), directory);
    }

    private static PersistentHashRegistry RegistryOn(string directory)
    {
        var options = Options.Create(new EventStreamOptions
        {
            HashRegistryDirectory = directory
        });
        return new PersistentHashRegistry(options, null);
    }

    [GeneratedTest("EventStream/Persistent-Hash-Register-Roundtrips-Stored-Blob", "global::Atelier.Framework.EventStream.PersistentHashRegistry")]
    public static async Task RegisterThenLookupReturnsStoredBlob()
    {
        var (registry, _) = NewRegistry();
        var blob = new byte[] { 10, 20, 30, 40 };
        var hash = DigestOf(blob);

        var registered = await registry.RegisterAsync(hash, blob).ConfigureAwait(false);
        if (!registered.IsSuccess)
        {
            throw new InvalidOperationException("register failed");
        }

        var lookup = await registry.LookupAsync(hash).ConfigureAwait(false);
        if (lookup.Data is null
            || !lookup.Data.AsSpan().SequenceEqual(blob))
        {
            throw new InvalidOperationException("lookup returned a blob that does not match the stored bytes");
        }
    }

    [GeneratedTest("EventStream/Persistent-Hash-Survives-Reinitialize", "global::Atelier.Framework.EventStream.PersistentHashRegistry")]
    public static async Task RegisteredBlobIsRestoredByAFreshRegistryOverSameDirectory()
    {
        var (registry, directory) = NewRegistry();
        var blob = new byte[] { 1, 1, 2, 3, 5, 8 };
        var hash = DigestOf(blob);

        await registry.RegisterAsync(hash, blob).ConfigureAwait(false);

        var reopened = RegistryOn(directory);
        var initialized = await reopened.InitializeAsync().ConfigureAwait(false);
        if (!initialized.IsSuccess)
        {
            throw new InvalidOperationException("initialize failed");
        }

        var lookup = await reopened.LookupAsync(hash).ConfigureAwait(false);
        if (lookup.Data is null
            || !lookup.Data.AsSpan().SequenceEqual(blob))
        {
            throw new InvalidOperationException("reopened registry did not restore the persisted blob");
        }
    }

    [GeneratedTest("EventStream/Persistent-Hash-Released-Blob-Stays-Gone-After-Reinitialize", "global::Atelier.Framework.EventStream.PersistentHashRegistry")]
    public static async Task ReleasedBlobDoesNotResurrectAfterReinitialize()
    {
        var (registry, directory) = NewRegistry();
        var blob = new byte[] { 4, 4, 4, 4 };
        var hash = DigestOf(blob);

        await registry.RegisterAsync(hash, blob).ConfigureAwait(false);
        var released = await registry.ReleaseAsync(hash).ConfigureAwait(false);
        if (!released.IsSuccess
            || released.Data != 0)
        {
            throw new InvalidOperationException($"expected reference count 0 after release, got {released.Data}");
        }

        var reopened = RegistryOn(directory);
        await reopened.InitializeAsync().ConfigureAwait(false);

        var exists = await reopened.ExistsAsync(hash).ConfigureAwait(false);
        if (exists.Data)
        {
            throw new InvalidOperationException("a released hash resurrected on reinitialize; its blob was not deleted from disk");
        }
    }

    [GeneratedTest("EventStream/Persistent-Hash-Rejects-Tampered-Blob-On-Reinitialize", "global::Atelier.Framework.EventStream.PersistentHashRegistry")]
    public static async Task ReinitializeRejectsBlobWhoseContentNoLongerMatchesItsHash()
    {
        var directory = Path.Combine(Path.GetTempPath(), "atelier-test", "hashes", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var blob = new byte[] { 2, 4, 6, 8 };
        var hash = DigestOf(blob);

        var tampered = new byte[] { 9, 9, 9, 9 };
        var payload = new byte[sizeof(int) + tampered.Length];
        BitConverter.GetBytes(1).CopyTo(payload, 0);
        tampered.CopyTo(payload, sizeof(int));
        await File.WriteAllBytesAsync(Path.Combine(directory, $"{hash}.blob"), payload).ConfigureAwait(false);

        var registry = RegistryOn(directory);
        var initialized = await registry.InitializeAsync().ConfigureAwait(false);
        if (!initialized.IsSuccess)
        {
            throw new InvalidOperationException("initialize failed");
        }

        var exists = await registry.ExistsAsync(hash).ConfigureAwait(false);
        if (exists.Data)
        {
            throw new InvalidOperationException("a blob whose content no longer matches its hash key was restored");
        }
    }
}
