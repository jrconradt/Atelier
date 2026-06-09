using Atelier.Framework.Attributes;
using Atelier.Framework.Outcomes;

namespace Atelier.Framework.EventStream.Consumers.Services;

public interface IOffsetManagerService
{
    public Task<Outcome<long>> GetStartingOffsetAsync(
        string consumerGroup,
        string topic,
        CancellationToken cancellationToken = default);

    public Task<Outcome> CommitOffsetAsync(
        string consumerGroup,
        string topic,
        long offset,
        CancellationToken cancellationToken = default);

    public Task<Outcome> CommitOffsetsAsync(
        string consumerGroup,
        Dictionary<string, long> topicOffsets,
        CancellationToken cancellationToken = default);

    public Task<Outcome<int>> DeleteOffsetsForConsumerAsync(
        string consumerGroup,
        CancellationToken cancellationToken = default);
}
