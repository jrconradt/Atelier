using Atelier.Framework.Outcomes;

namespace Atelier.Framework.EventStream.Core;

public interface IEventStream
{
    public Task<Outcome<long>> AppendAsync(
        string topic,
        byte[] payload,
        EventMetadata? metadata,
        CancellationToken ct);

    public IAsyncEnumerable<StreamEvent> ReadAsync(
        string topic,
        long fromOffset,
        int batchSize,
        CancellationToken ct);

    public Task<Outcome> CommitOffsetAsync(
        string consumerGroup,
        string topic,
        long offset,
        CancellationToken ct);

    public Task<Outcome<long>> GetOffsetAsync(
        string consumerGroup,
        string topic,
        CancellationToken ct);

    public Task<Outcome<StreamStats>> GetStatsAsync(
        string topic,
        CancellationToken ct);

    public Task<Outcome> EnforceRetentionAsync(
        string topic,
        CancellationToken ct);
}
