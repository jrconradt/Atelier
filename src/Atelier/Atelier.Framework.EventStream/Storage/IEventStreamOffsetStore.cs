using Atelier.Framework.Outcomes;

namespace Atelier.Framework.EventStream.Storage;

public interface IEventStreamOffsetStore
{
    public Task<Outcome> InitializeAsync(CancellationToken ct);

    public Task<Outcome> CommitOffsetAsync(
        string consumerGroup,
        string topic,
        long offset,
        CancellationToken ct);

    public Task<Outcome<int>> CommitOffsetsAsync(
        string consumerGroup,
        IReadOnlyDictionary<string, long> topicOffsets,
        CancellationToken ct);

    public Task<Outcome<long>> GetOffsetAsync(
        string consumerGroup,
        string topic,
        CancellationToken ct);

    public Task<Outcome<Dictionary<string, long>>> GetAllOffsetsForConsumerAsync(
        string consumerGroup,
        CancellationToken ct);

    public Task<Outcome> RemoveOffsetAsync(
        string consumerGroup,
        string topic,
        CancellationToken ct);

    public Task<Outcome<int>> DeleteOffsetsForConsumerAsync(
        string consumerGroup,
        CancellationToken ct);
}
