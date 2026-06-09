using System.Security.Cryptography;
using Atelier.Framework.Testing;

namespace Atelier.Framework.EventStream;

public static class HashRegistryBehaviorTests
{
    private static string DigestOf(byte[] blob)
    {
        return Convert.ToHexStringLower(SHA256.HashData(blob));
    }

    [GeneratedTest("EventStream/Hash-Register-Roundtrips-Stored-Blob", "global::Atelier.Framework.EventStream.InMemoryHashRegistry")]
    public static async Task RegisterThenLookupReturnsStoredBlob()
    {
        var registry = new InMemoryHashRegistry(null);
        var blob = new byte[] { 1, 2, 3, 4, 5 };
        var hash = DigestOf(blob);

        var registered = await registry.RegisterAsync(hash, blob).ConfigureAwait(false);
        if (!registered.IsSuccess)
        {
            throw new InvalidOperationException("register failed");
        }
        if (registered.Data != hash)
        {
            throw new InvalidOperationException($"register returned '{registered.Data}', expected '{hash}'");
        }

        var lookup = await registry.LookupAsync(hash).ConfigureAwait(false);
        if (!lookup.IsSuccess)
        {
            throw new InvalidOperationException("lookup failed");
        }
        if (lookup.Data is null
            || !lookup.Data.AsSpan().SequenceEqual(blob))
        {
            throw new InvalidOperationException("lookup returned a blob that does not match the stored bytes");
        }
    }

    [GeneratedTest("EventStream/Hash-Mismatch-Is-Rejected", "global::Atelier.Framework.EventStream.InMemoryHashRegistry")]
    public static async Task RegisterRejectsBlobThatDoesNotMatchSuppliedHash()
    {
        var registry = new InMemoryHashRegistry(null);
        var blob = new byte[] { 9, 9, 9 };
        var wrongHash = DigestOf(new byte[] { 0 });

        var registered = await registry.RegisterAsync(wrongHash, blob).ConfigureAwait(false);
        if (registered.IsSuccess)
        {
            throw new InvalidOperationException("register accepted a blob whose digest does not match the supplied hash");
        }

        var exists = await registry.ExistsAsync(wrongHash).ConfigureAwait(false);
        if (exists.Data)
        {
            throw new InvalidOperationException("a rejected mismatched registration still stored its hash");
        }
    }

    [GeneratedTest("EventStream/Hash-Reference-Counting-Tracks-Register-And-Release", "global::Atelier.Framework.EventStream.InMemoryHashRegistry")]
    public static async Task ReferenceCountIncrementsOnReRegisterAndDecrementsOnRelease()
    {
        var registry = new InMemoryHashRegistry(null);
        var blob = new byte[] { 7, 7 };
        var hash = DigestOf(blob);

        await registry.RegisterAsync(hash, blob).ConfigureAwait(false);
        await registry.RegisterAsync(hash, blob).ConfigureAwait(false);

        var afterTwo = await registry.GetReferenceCountAsync(hash).ConfigureAwait(false);
        if (afterTwo.Data != 2)
        {
            throw new InvalidOperationException($"expected reference count 2 after two registers, got {afterTwo.Data}");
        }

        var firstRelease = await registry.ReleaseAsync(hash).ConfigureAwait(false);
        if (firstRelease.Data != 1)
        {
            throw new InvalidOperationException($"expected reference count 1 after first release, got {firstRelease.Data}");
        }

        var stillPresent = await registry.ExistsAsync(hash).ConfigureAwait(false);
        if (!stillPresent.Data)
        {
            throw new InvalidOperationException("hash was removed while a reference was still held");
        }

        var secondRelease = await registry.ReleaseAsync(hash).ConfigureAwait(false);
        if (secondRelease.Data != 0)
        {
            throw new InvalidOperationException($"expected reference count 0 after final release, got {secondRelease.Data}");
        }

        var gone = await registry.ExistsAsync(hash).ConfigureAwait(false);
        if (gone.Data)
        {
            throw new InvalidOperationException("hash remained after its last reference was released");
        }
    }

    [GeneratedTest("EventStream/Hash-Store-Rejects-Register-When-Full-Of-Held-Entries", "global::Atelier.Framework.EventStream.HashRegistryStore")]
    public static void RegisterIsRejectedWhenStoreIsAtCapacityWithNoEvictableEntries()
    {
        var store = new HashRegistryStore(maxCacheSize: 2);

        var first = new byte[] { 1 };
        var second = new byte[] { 2 };
        store.Register(DigestOf(first), first);
        store.Register(DigestOf(second), second);

        var third = new byte[] { 3 };
        var overflow = store.Register(DigestOf(third), third);

        if (overflow.Status != HashRegisterStatus.CapacityExceeded)
        {
            throw new InvalidOperationException($"expected CapacityExceeded when the store is full of held entries, got {overflow.Status}");
        }

        if (store.Contains(DigestOf(third)))
        {
            throw new InvalidOperationException("a rejected registration still added its hash to the store");
        }
    }

    [GeneratedTest("EventStream/Hash-Store-Allows-Reregister-Of-Existing-Hash-At-Capacity", "global::Atelier.Framework.EventStream.HashRegistryStore")]
    public static void ReRegisterOfAlreadyHeldHashSucceedsEvenAtCapacity()
    {
        var store = new HashRegistryStore(maxCacheSize: 1);

        var blob = new byte[] { 7 };
        var hash = DigestOf(blob);
        store.Register(hash, blob);

        var again = store.Register(hash, blob);

        if (again.Status != HashRegisterStatus.Registered)
        {
            throw new InvalidOperationException($"expected re-register of an existing hash to succeed at capacity, got {again.Status}");
        }

        if (again.RefCount != 2)
        {
            throw new InvalidOperationException($"expected reference count 2 after re-register, got {again.RefCount}");
        }
    }

    [GeneratedTest("EventStream/Hash-Release-Underflow-Is-Signalled", "global::Atelier.Framework.EventStream.HashRegistryStore")]
    public static void ReleaseOfZeroRefCountEntryReportsUnderflowInsteadOfLastRelease()
    {
        var store = new HashRegistryStore(maxCacheSize: 4);

        var blob = new byte[] { 5, 5, 5 };
        var hash = DigestOf(blob);
        store.Restore([new HashSnapshotEntry(hash, blob, 0, 0)]);

        var release = store.Release(hash);

        if (release.Removed)
        {
            throw new InvalidOperationException("release of a zero-refcount entry was reported as a legitimate last release");
        }

        if (!release.Underflowed)
        {
            throw new InvalidOperationException("release of a zero-refcount entry did not signal underflow");
        }

        if (store.Contains(hash))
        {
            throw new InvalidOperationException("zero-refcount entry remained after underflow release");
        }
    }
}
