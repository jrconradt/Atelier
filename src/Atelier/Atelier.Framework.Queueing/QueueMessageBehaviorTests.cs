using Atelier.Framework.Queueing.Core;
using Atelier.Framework.Testing;

namespace Atelier.Framework.Queueing;

public static class QueueMessageBehaviorTests
{
    [GeneratedTest("Queueing/Payload-Serializes-And-Round-Trips", "global::Atelier.Framework.Queueing.Core.QueueMessage")]
    public static void ObjectPayloadRoundTripsThroughDeserialize()
    {
        var message = new QueueMessage("order.created", new[] { 3, 1, 4, 1, 5 });

        var recovered = message.DeserializePayload<int[]>();
        if (recovered.Length != 5
            || recovered[0] != 3
            || recovered[4] != 5)
        {
            throw new InvalidOperationException($"payload round-trip yielded [{string.Join(", ", recovered)}], expected [3, 1, 4, 1, 5]");
        }
    }

    [GeneratedTest("Queueing/Create-Retry-Increments-Count-And-Preserves-Identity", "global::Atelier.Framework.Queueing.Core.QueueMessage")]
    public static void CreateRetryIncrementsRetryCountWhileKeepingIdAndType()
    {
        var original = new QueueMessage("order.created", "payload")
        {
            MaxRetries = 5
        };

        var retried = original.CreateRetry();

        if (retried.RetryCount != original.RetryCount + 1)
        {
            throw new InvalidOperationException($"CreateRetry set RetryCount to {retried.RetryCount}, expected {original.RetryCount + 1}");
        }
        if (retried.Id != original.Id)
        {
            throw new InvalidOperationException($"CreateRetry changed Id from '{original.Id}' to '{retried.Id}'");
        }
        if (retried.MessageType != "order.created"
            || retried.Payload != original.Payload)
        {
            throw new InvalidOperationException("CreateRetry altered the message type or payload");
        }
        if (original.RetryCount != 0)
        {
            throw new InvalidOperationException($"CreateRetry mutated the original message, RetryCount is now {original.RetryCount}");
        }
    }

    [GeneratedTest("Queueing/With-Updates-Produces-Independent-Copy", "global::Atelier.Framework.Queueing.Core.QueueMessage")]
    public static void WithUpdatesLeavesTheSourceUntouched()
    {
        var original = new QueueMessage("order.created", "payload")
        {
            Priority = 1
        };
        original.Headers["tenant"] = "acme";

        var copy = original.WithUpdates(m =>
        {
            m.Priority = 9;
            m.Headers["tenant"] = "globex";
        });

        if (copy.Priority != 9)
        {
            throw new InvalidOperationException($"copy carried priority {copy.Priority}, expected 9");
        }
        if (original.Priority != 1)
        {
            throw new InvalidOperationException($"original priority was mutated to {original.Priority}, expected 1");
        }
        if (original.Headers["tenant"] != "acme")
        {
            throw new InvalidOperationException($"original header was mutated to '{original.Headers["tenant"]}', expected 'acme'");
        }
        if (copy.Headers["tenant"] != "globex")
        {
            throw new InvalidOperationException($"copy header was '{copy.Headers["tenant"]}', expected 'globex'");
        }
    }
}
