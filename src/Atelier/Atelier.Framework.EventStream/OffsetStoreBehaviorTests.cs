using Atelier.Framework.EventStream.Configuration;
using Atelier.Framework.EventStream.Storage;
using Atelier.Framework.Testing;
using Microsoft.Extensions.Options;

namespace Atelier.Framework.EventStream;

public static class OffsetStoreBehaviorTests
{
    private static EventStreamOffsetStore NewStore()
    {
        return StoreOn(Path.Combine(Path.GetTempPath(), "atelier-test", "offsets", Guid.NewGuid().ToString("N")));
    }

    private static EventStreamOffsetStore StoreOn(string directory)
    {
        var options = Options.Create(new EventStreamOptions
        {
            OffsetStoreDirectory = directory
        });
        return new EventStreamOffsetStore(options, null);
    }

    [GeneratedTest("EventStream/Offset-Defaults-To-Zero-When-Uncommitted", "global::Atelier.Framework.EventStream.Storage.EventStreamOffsetStore")]
    public static async Task GetOffsetReturnsZeroForUnknownTopic()
    {
        var store = NewStore();

        var offset = await store.GetOffsetAsync("group-a", "topic-x", CancellationToken.None).ConfigureAwait(false);
        if (!offset.IsSuccess)
        {
            throw new InvalidOperationException("get failed");
        }
        if (offset.Data != 0L)
        {
            throw new InvalidOperationException($"expected default offset 0, got {offset.Data}");
        }
    }

    [GeneratedTest("EventStream/Offset-Commit-Is-Readable", "global::Atelier.Framework.EventStream.Storage.EventStreamOffsetStore")]
    public static async Task CommitThenGetReturnsCommittedOffset()
    {
        var store = NewStore();

        var committed = await store.CommitOffsetAsync("group-a", "topic-x", 42L, CancellationToken.None).ConfigureAwait(false);
        if (!committed.IsSuccess)
        {
            throw new InvalidOperationException("commit failed");
        }

        var offset = await store.GetOffsetAsync("group-a", "topic-x", CancellationToken.None).ConfigureAwait(false);
        if (offset.Data != 42L)
        {
            throw new InvalidOperationException($"expected offset 42, got {offset.Data}");
        }
    }

    [GeneratedTest("EventStream/Offset-Commit-Is-Monotonic", "global::Atelier.Framework.EventStream.Storage.EventStreamOffsetStore")]
    public static async Task CommitAdvancesForwardAndKeepsHighWaterMark()
    {
        var store = NewStore();

        await store.CommitOffsetAsync("group-a", "topic-x", 10L, CancellationToken.None).ConfigureAwait(false);
        var forward = await store.CommitOffsetAsync("group-a", "topic-x", 20L, CancellationToken.None).ConfigureAwait(false);
        if (!forward.IsSuccess)
        {
            throw new InvalidOperationException("forward commit failed");
        }

        var offset = await store.GetOffsetAsync("group-a", "topic-x", CancellationToken.None).ConfigureAwait(false);
        if (offset.Data != 20L)
        {
            throw new InvalidOperationException($"expected high-water-mark 20, got {offset.Data}");
        }
    }

    [GeneratedTest("EventStream/Offset-Regression-Is-Rejected", "global::Atelier.Framework.EventStream.Storage.EventStreamOffsetStore")]
    public static async Task CommitBelowCommittedOffsetFailsAndPreservesHighWaterMark()
    {
        var store = NewStore();

        await store.CommitOffsetAsync("group-a", "topic-x", 30L, CancellationToken.None).ConfigureAwait(false);
        var regressed = await store.CommitOffsetAsync("group-a", "topic-x", 15L, CancellationToken.None).ConfigureAwait(false);
        if (regressed.IsSuccess)
        {
            throw new InvalidOperationException("commit accepted an offset that regresses below the committed high-water-mark");
        }

        var offset = await store.GetOffsetAsync("group-a", "topic-x", CancellationToken.None).ConfigureAwait(false);
        if (offset.Data != 30L)
        {
            throw new InvalidOperationException($"expected high-water-mark to remain 30 after a rejected regression, got {offset.Data}");
        }
    }

    [GeneratedTest("EventStream/Offset-Batch-Commit-Counts-Topics", "global::Atelier.Framework.EventStream.Storage.EventStreamOffsetStore")]
    public static async Task BatchCommitReturnsCommittedCountAndPersistsEachTopic()
    {
        var store = NewStore();

        var batch = new Dictionary<string, long>
        {
            ["topic-x"] = 5L,
            ["topic-y"] = 9L,
            ["topic-z"] = 12L
        };

        var result = await store.CommitOffsetsAsync("group-b", batch, CancellationToken.None).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException("batch commit failed");
        }
        if (result.Data != 3)
        {
            throw new InvalidOperationException($"expected 3 committed topics, got {result.Data}");
        }

        var y = await store.GetOffsetAsync("group-b", "topic-y", CancellationToken.None).ConfigureAwait(false);
        if (y.Data != 9L)
        {
            throw new InvalidOperationException($"expected topic-y at offset 9, got {y.Data}");
        }
    }

    [GeneratedTest("EventStream/Offset-Batch-Commit-Reports-Regression", "global::Atelier.Framework.EventStream.Storage.EventStreamOffsetStore")]
    public static async Task BatchCommitFailsWhenAnyTopicRegresses()
    {
        var store = NewStore();

        await store.CommitOffsetAsync("group-b", "topic-x", 50L, CancellationToken.None).ConfigureAwait(false);

        var batch = new Dictionary<string, long>
        {
            ["topic-x"] = 25L
        };

        var result = await store.CommitOffsetsAsync("group-b", batch, CancellationToken.None).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            throw new InvalidOperationException("batch commit accepted a regressing topic offset");
        }

        var preserved = await store.GetOffsetAsync("group-b", "topic-x", CancellationToken.None).ConfigureAwait(false);
        if (preserved.Data != 50L)
        {
            throw new InvalidOperationException($"expected high-water-mark to remain 50 after a rejected batch regression, got {preserved.Data}");
        }
    }

    [GeneratedTest("EventStream/Offset-Get-All-Returns-Committed-Topics", "global::Atelier.Framework.EventStream.Storage.EventStreamOffsetStore")]
    public static async Task GetAllForConsumerReturnsEveryCommittedTopic()
    {
        var store = NewStore();

        await store.CommitOffsetAsync("group-c", "topic-x", 1L, CancellationToken.None).ConfigureAwait(false);
        await store.CommitOffsetAsync("group-c", "topic-y", 2L, CancellationToken.None).ConfigureAwait(false);

        var all = await store.GetAllOffsetsForConsumerAsync("group-c", CancellationToken.None).ConfigureAwait(false);
        if (!all.IsSuccess)
        {
            throw new InvalidOperationException("get-all failed");
        }
        if (all.Data is null
            || all.Data.Count != 2)
        {
            throw new InvalidOperationException($"expected 2 committed topics, got {all.Data?.Count ?? -1}");
        }
        if (!all.Data.TryGetValue("topic-x", out var x)
            || x != 1L)
        {
            throw new InvalidOperationException($"expected topic-x at offset 1, got {(all.Data.TryGetValue("topic-x", out var got) ? got : -1L)}");
        }
        if (!all.Data.TryGetValue("topic-y", out var y)
            || y != 2L)
        {
            throw new InvalidOperationException($"expected topic-y at offset 2, got {(all.Data.TryGetValue("topic-y", out var got) ? got : -1L)}");
        }
    }

    [GeneratedTest("EventStream/Offset-Remove-Clears-Single-Topic", "global::Atelier.Framework.EventStream.Storage.EventStreamOffsetStore")]
    public static async Task RemoveDropsOneTopicAndLeavesOthers()
    {
        var store = NewStore();

        await store.CommitOffsetAsync("group-d", "topic-x", 7L, CancellationToken.None).ConfigureAwait(false);
        await store.CommitOffsetAsync("group-d", "topic-y", 8L, CancellationToken.None).ConfigureAwait(false);

        var removed = await store.RemoveOffsetAsync("group-d", "topic-x", CancellationToken.None).ConfigureAwait(false);
        if (!removed.IsSuccess)
        {
            throw new InvalidOperationException("remove failed");
        }

        var goneOffset = await store.GetOffsetAsync("group-d", "topic-x", CancellationToken.None).ConfigureAwait(false);
        if (goneOffset.Data != 0L)
        {
            throw new InvalidOperationException($"expected removed topic to read back as 0, got {goneOffset.Data}");
        }

        var all = await store.GetAllOffsetsForConsumerAsync("group-d", CancellationToken.None).ConfigureAwait(false);
        if (all.Data is null
            || all.Data.Count != 1
            || !all.Data.ContainsKey("topic-y"))
        {
            throw new InvalidOperationException("expected only topic-y to remain after removing topic-x");
        }
    }

    [GeneratedTest("EventStream/Offset-Delete-For-Consumer-Counts-Removed", "global::Atelier.Framework.EventStream.Storage.EventStreamOffsetStore")]
    public static async Task DeleteForConsumerRemovesAllTopicsAndReturnsCount()
    {
        var store = NewStore();

        await store.CommitOffsetAsync("group-e", "topic-x", 3L, CancellationToken.None).ConfigureAwait(false);
        await store.CommitOffsetAsync("group-e", "topic-y", 4L, CancellationToken.None).ConfigureAwait(false);
        await store.CommitOffsetAsync("group-e", "topic-z", 5L, CancellationToken.None).ConfigureAwait(false);

        var deleted = await store.DeleteOffsetsForConsumerAsync("group-e", CancellationToken.None).ConfigureAwait(false);
        if (!deleted.IsSuccess)
        {
            throw new InvalidOperationException("delete failed");
        }
        if (deleted.Data != 3)
        {
            throw new InvalidOperationException($"expected 3 deleted topics, got {deleted.Data}");
        }

        var all = await store.GetAllOffsetsForConsumerAsync("group-e", CancellationToken.None).ConfigureAwait(false);
        if (all.Data is null
            || all.Data.Count != 0)
        {
            throw new InvalidOperationException($"expected no topics after delete, got {all.Data?.Count ?? -1}");
        }
    }

    [GeneratedTest("EventStream/Offset-Groups-Differing-Only-By-Punctuation-Do-Not-Collide", "global::Atelier.Framework.EventStream.Storage.EventStreamOffsetStore")]
    public static async Task GroupsThatSanitizeToTheSameStemPersistIndependently()
    {
        var directory = Path.Combine(Path.GetTempPath(), "atelier-test", "offsets", Guid.NewGuid().ToString("N"));
        var store = StoreOn(directory);

        await store.CommitOffsetAsync("orders.v1", "topic-x", 11L, CancellationToken.None).ConfigureAwait(false);
        await store.CommitOffsetAsync("orders/v1", "topic-x", 22L, CancellationToken.None).ConfigureAwait(false);

        var first = await store.GetOffsetAsync("orders.v1", "topic-x", CancellationToken.None).ConfigureAwait(false);
        var second = await store.GetOffsetAsync("orders/v1", "topic-x", CancellationToken.None).ConfigureAwait(false);
        if (first.Data != 11L
            || second.Data != 22L)
        {
            throw new InvalidOperationException($"groups collided in memory: orders.v1={first.Data}, orders/v1={second.Data}");
        }

        var reopened = StoreOn(directory);
        var initialized = await reopened.InitializeAsync(CancellationToken.None).ConfigureAwait(false);
        if (!initialized.IsSuccess)
        {
            throw new InvalidOperationException("initialize failed");
        }

        var restoredFirst = await reopened.GetOffsetAsync("orders.v1", "topic-x", CancellationToken.None).ConfigureAwait(false);
        var restoredSecond = await reopened.GetOffsetAsync("orders/v1", "topic-x", CancellationToken.None).ConfigureAwait(false);
        if (restoredFirst.Data != 11L
            || restoredSecond.Data != 22L)
        {
            throw new InvalidOperationException($"groups collided on disk: orders.v1={restoredFirst.Data}, orders/v1={restoredSecond.Data}");
        }
    }
}
