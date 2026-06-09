# Event stream

The event stream is a topic-based, offset-tracked consumption model. A consumer implements `IEventStreamConsumer`, declares the topics it reads, and the framework drives a consume loop that reads events in offset order, dispatches each to the consumer, and commits progress to a durable offset store.

## Consumers

A consumer implements `IEventStreamConsumer`:

```csharp
public sealed class OrderProjectionConsumer : IEventStreamConsumer
{
    public string ConsumerName => "order-projection";
    public string ConsumerGroup => "projections";
    public IEnumerable<string> Topics => new[] { "orders" };

    public Task<Outcome> ProcessEventAsync(
        StreamEvent streamEvent,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Outcome.Success());
    }
}
```

`ProcessEventAsync` returns `Outcome` — a failure leaves the offset uncommitted and triggers a bounded retry of the same offset before the event is skipped as poison.

## Delivery semantics

Delivery is **at-least-once**. The consume loop runs the side effect first, then marks the event processed, then commits the offset:

1. `ProcessEventAsync` is awaited for the event at `currentOffset`.
2. On success the event is recorded in the in-run dedup set and `currentOffset` advances by exactly one.
3. The committed offset is persisted to the offset store once every `ConsumerCommitInterval` events.

Because the side effect commits before the offset, a crash or cancellation between the side effect and the offset commit re-reads and re-dispatches every event from the last committed offset forward on restart. The in-run dedup set is held per processor instance and does not survive a restart, so it does not protect against post-restart replay.

### Idempotency is a consumer obligation

`IEventStreamConsumer.ProcessEventAsync` must be idempotent. Processing the same `StreamEvent` more than once must converge to the same observable state as processing it once. Key the consumer's effect on the event's identity (`StreamEvent.Offset` within a topic, or the event's correlation identifier) and make the write a conditional upsert, a deduplicated insert, or an otherwise replay-safe operation. A consumer that performs a non-idempotent side effect — incrementing a counter, appending a row, sending an unconditional notification — will double-apply that effect across the replay window.

## Offsets and the offset store

`IEventStreamOffsetStore` records the committed offset per `(ConsumerGroup, Topic)` and persists each consumer group to its own file under `OffsetStoreDirectory`. `GetOffsetAsync` returns the last committed offset (zero when none exists), and the consume loop resumes from there.

`CommitOffsetAsync` keeps offsets monotonic: it takes the maximum of the stored and supplied offset and returns `Outcome.Failure()`, logging a regression warning, when the supplied offset is below the stored one. Monotonicity is the only invariant the store enforces on its own.

### Commits must reflect contiguously-processed offsets

The store records a high-water mark; it has no notion of which offsets between the previous commit and the new one were actually delivered. A caller that commits an offset ahead of what it processed — committing `1000` after processing through `10` — locks in `1000`, and on restart the consume loop resumes at `1000` and never delivers `11`–`999`. With at-least-once delivery the committed offset must reflect the highest **contiguously-processed** offset: the framework consume loop satisfies this by advancing `currentOffset` by exactly one per processed event and committing that value, so callers driving the store directly must preserve the same contiguity.

## See also

- [Outcomes](outcomes.md) — `ProcessEventAsync` returns `Outcome`.
- [Requisites](requisites.md) — consumers and the offset store receive dependencies as `[Requisite]` fields.
- [Observability](observability.md) — the offset store reports commits and regressions through `Observe(...)`.
