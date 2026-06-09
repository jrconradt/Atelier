using Atelier.Framework.Queueing.Core;
using Atelier.Framework.Queueing.Orchestration;
using Atelier.Framework.Testing;

namespace Atelier.Framework.Queueing;

public static class QueueManagerBehaviorTests
{
    [GeneratedTest("Queueing/Create-Then-Get-Returns-Same-Queue-And-Lists-It", "global::Atelier.Framework.Queueing.Orchestration.QueueManager")]
    public static async Task CreateQueueIsResolvableAndEnumerated()
    {
        using var manager = new QueueManager();

        var created = await manager.CreateQueueAsync("orders").ConfigureAwait(false);
        if (!created.IsSuccess)
        {
            throw new InvalidOperationException("CreateQueueAsync failed for a fresh queue name");
        }
        if (created.Data!.Name != "orders")
        {
            throw new InvalidOperationException($"created queue named '{created.Data!.Name}', expected 'orders'");
        }

        var fetched = await manager.GetQueueAsync("orders").ConfigureAwait(false);
        if (!ReferenceEquals(fetched.Data, created.Data))
        {
            throw new InvalidOperationException("GetQueueAsync returned a different instance than CreateQueueAsync");
        }

        var queues = await manager.GetQueuesAsync().ConfigureAwait(false);
        var names = queues.Select(q => q.Name).ToList();
        if (names.Count != 1
            || names[0] != "orders")
        {
            throw new InvalidOperationException($"GetQueuesAsync returned [{string.Join(", ", names)}], expected [orders]");
        }
    }

    [GeneratedTest("Queueing/Duplicate-Create-Is-Rejected", "global::Atelier.Framework.Queueing.Orchestration.QueueManager")]
    public static async Task CreatingTheSameQueueTwiceFails()
    {
        using var manager = new QueueManager();

        await manager.CreateQueueAsync("dupes").ConfigureAwait(false);
        var second = await manager.CreateQueueAsync("dupes").ConfigureAwait(false);

        if (second.IsSuccess)
        {
            throw new InvalidOperationException("second CreateQueueAsync for the same name succeeded");
        }
    }

    [GeneratedTest("Queueing/Get-Blank-Queue-Name-Is-Rejected", "global::Atelier.Framework.Queueing.Orchestration.QueueManager")]
    public static async Task GetQueueWithBlankNameFails()
    {
        using var manager = new QueueManager();

        var result = await manager.GetQueueAsync("   ").ConfigureAwait(false);
        if (result.IsSuccess)
        {
            throw new InvalidOperationException("GetQueueAsync accepted a blank queue name");
        }
    }

    [GeneratedTest("Queueing/Delete-Removes-Queue-And-Missing-Delete-Is-Idempotent", "global::Atelier.Framework.Queueing.Orchestration.QueueManager")]
    public static async Task DeleteRemovesQueueAndMissingDeleteIsIdempotent()
    {
        using var manager = new QueueManager();

        var missing = await manager.DeleteQueueAsync("ghost").ConfigureAwait(false);
        if (!missing.IsSuccess)
        {
            throw new InvalidOperationException("DeleteQueueAsync of an absent queue did not return idempotent success");
        }

        await manager.CreateQueueAsync("transient").ConfigureAwait(false);
        var deleted = await manager.DeleteQueueAsync("transient").ConfigureAwait(false);
        if (!deleted.IsSuccess)
        {
            throw new InvalidOperationException("delete of existing queue did not succeed");
        }

        var remaining = await manager.GetQueuesAsync().ConfigureAwait(false);
        if (remaining.Any(q => q.Name == "transient"))
        {
            throw new InvalidOperationException("queue 'transient' was still present after deletion");
        }

        var deletedAgain = await manager.DeleteQueueAsync("transient").ConfigureAwait(false);
        if (!deletedAgain.IsSuccess)
        {
            throw new InvalidOperationException("second delete of an already-removed queue did not return idempotent success");
        }
    }

    [GeneratedTest("Queueing/Enqueue-Preserves-FIFO-Order", "global::Atelier.Framework.Queueing.Core.InMemoryQueue")]
    public static async Task EnqueuePreservesFifoOrderOnTheChannel()
    {
        using var manager = new QueueManager();
        var queue = (await manager.CreateQueueAsync("fifo").ConfigureAwait(false)).Data!;

        var first = await queue.EnqueueAsync("order.created", "first").ConfigureAwait(false);
        var second = await queue.EnqueueAsync("order.created", "second").ConfigureAwait(false);

        if (!first.IsSuccess
            || !second.IsSuccess)
        {
            throw new InvalidOperationException("enqueue onto the FIFO channel failed");
        }

        var read1 = await queue.Channel.Reader.ReadAsync().ConfigureAwait(false);
        var read2 = await queue.Channel.Reader.ReadAsync().ConfigureAwait(false);

        if (read1.DeserializePayload<string>() != "first"
            || read2.DeserializePayload<string>() != "second")
        {
            throw new InvalidOperationException($"channel delivered [{read1.DeserializePayload<string>()}, {read2.DeserializePayload<string>()}] out of FIFO order");
        }
    }

    [GeneratedTest("Queueing/Unsupported-Delivery-Option-Is-Rejected", "global::Atelier.Framework.Queueing.Core.InMemoryQueue")]
    public static async Task PriorityOptionIsRejectedByInMemoryQueue()
    {
        using var manager = new QueueManager();
        var queue = (await manager.CreateQueueAsync("prio").ConfigureAwait(false)).Data!;

        var result = await queue.EnqueueAsync(
            "order.created",
            "payload",
            new QueueMessageOptions
            {
                Priority = 7
            }).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            throw new InvalidOperationException("in-memory FIFO queue accepted a priority delivery option");
        }
    }

    [GeneratedTest("Queueing/Full-Bounded-Queue-Times-Out", "global::Atelier.Framework.Queueing.Core.InMemoryQueue")]
    public static async Task EnqueueOntoFullBoundedQueueReportsQueueFull()
    {
        using var manager = new QueueManager();
        var queue = (await manager.CreateQueueAsync(
            "tiny",
            new QueueConfiguration
            {
                MaxCapacity = 1
            }).ConfigureAwait(false)).Data!;

        var accepted = await queue.EnqueueAsync("order.created", "fills-the-queue").ConfigureAwait(false);
        if (!accepted.IsSuccess)
        {
            throw new InvalidOperationException("first enqueue should have fit into the bounded queue");
        }

        var overflowed = await ((InMemoryQueue)queue).EnqueueAsync(
            "order.created",
            "overflow",
            TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);

        if (overflowed.IsSuccess)
        {
            throw new InvalidOperationException("enqueue onto a full bounded queue succeeded");
        }
    }

    [GeneratedTest("Queueing/Processing-Telemetry-Tracks-Completed-And-Failed", "global::Atelier.Framework.Queueing.Orchestration.QueueManager")]
    public static async Task TelemetrySurfacesThroughQueueStats()
    {
        using var manager = new QueueManager();
        var queue = (await manager.CreateQueueAsync("telemetry").ConfigureAwait(false)).Data!;

        manager.RecordMessageRead("telemetry");
        manager.RecordProcessingResult("telemetry", true, 40);
        manager.RecordMessageRead("telemetry");
        manager.RecordProcessingResult("telemetry", false, 60);

        var stats = await queue.GetStatsAsync().ConfigureAwait(false);
        if (stats.CompletedCount != 1)
        {
            throw new InvalidOperationException($"expected 1 completed, got {stats.CompletedCount}");
        }
        if (stats.FailedCount != 1)
        {
            throw new InvalidOperationException($"expected 1 failed, got {stats.FailedCount}");
        }
        if (stats.AverageProcessingTimeMs != 50)
        {
            throw new InvalidOperationException($"expected average 50ms over two results, got {stats.AverageProcessingTimeMs}");
        }
    }
}
