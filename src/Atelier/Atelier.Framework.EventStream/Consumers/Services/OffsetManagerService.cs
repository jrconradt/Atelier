using Atelier.Framework.Primitives;
using Atelier.Framework.EventStream.Storage;
using Atelier.Framework.Infrastructure;
using Atelier.Framework.Attributes;
using Atelier.Framework.Infrastructure.Operation;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;

namespace Atelier.Framework.EventStream.Consumers.Services;

[Infrastructure(InfrastructureLifetime.Scoped)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class OffsetManagerService : IAtelier, IOffsetManagerService
{
    [Requisite] protected readonly EventStreamOffsetStore _offsetStore = null!;

    [Operation("GetStartingOffset")]
    public async Task<Outcome<long>> GetStartingOffsetAsync(
        string consumerGroup,
        string topic,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome<long>.Failure();
        }

        if (consumerGroup is null)
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", $"{nameof(consumerGroup)} was null")]);
            return Outcome<long>.Failure();
        }

        if (topic is null)
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", $"{nameof(topic)} was null"), ("ConsumerGroup", consumerGroup)]);
            return Outcome<long>.Failure();
        }

        var offsetResult = await _offsetStore.GetOffsetAsync(consumerGroup, topic, cancellationToken).ConfigureAwait(false);
        var startingOffset = offsetResult.IsSuccess ? offsetResult.Data : 0L;

        Observe(LogLevel.Information, values: [("ConsumerGroup", consumerGroup), ("Topic", topic), ("StartingOffset", startingOffset), ("OffsetFound", offsetResult.IsSuccess)]);

        return Outcome<long>.Success(startingOffset);
    }

    [Operation("CommitOffset")]
    public async Task<Outcome> CommitOffsetAsync(
        string consumerGroup,
        string topic,
        long offset,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome.Failure();
        }

        if (consumerGroup is null)
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", $"{nameof(consumerGroup)} was null")]);
            return Outcome.Failure();
        }

        if (topic is null)
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", $"{nameof(topic)} was null"), ("ConsumerGroup", consumerGroup)]);
            return Outcome.Failure();
        }

        var commitResult = await _offsetStore.CommitOffsetAsync(consumerGroup, topic, offset, cancellationToken).ConfigureAwait(false);

        if (!commitResult.IsSuccess)
        {
            Observe(LogLevel.Error, values: [("Reason", "Underlying offset commit failed"), ("ConsumerGroup", consumerGroup), ("Topic", topic), ("Offset", offset)]);

            return commitResult;
        }

        Observe(LogLevel.Information, values: [("ConsumerGroup", consumerGroup), ("Topic", topic), ("Offset", offset)]);

        return Outcome.Success();
    }

    [Operation("CommitOffsets")]
    public async Task<Outcome> CommitOffsetsAsync(
        string consumerGroup,
        Dictionary<string, long> topicOffsets,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome.Failure();
        }

        if (consumerGroup is null)
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", $"{nameof(consumerGroup)} was null")]);
            return Outcome.Failure();
        }

        if (topicOffsets is null)
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", $"{nameof(topicOffsets)} was null"), ("ConsumerGroup", consumerGroup)]);
            return Outcome.Failure();
        }

        var commitResult = await _offsetStore.CommitOffsetsAsync(consumerGroup, topicOffsets, cancellationToken).ConfigureAwait(false);

        if (!commitResult.IsSuccess)
        {
            Observe(LogLevel.Error, values: [("Reason", "Underlying batch offset commit failed"), ("ConsumerGroup", consumerGroup), ("TopicCount", topicOffsets.Count)]);

            return Outcome.Failure();
        }

        Observe(LogLevel.Information, values: [("ConsumerGroup", consumerGroup), ("TopicCount", topicOffsets.Count)]);

        return Outcome.Success();
    }

    [Operation("DeleteOffsetsForConsumer")]
    public async Task<Outcome<int>> DeleteOffsetsForConsumerAsync(
        string consumerGroup,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome<int>.Failure();
        }

        if (consumerGroup is null)
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", $"{nameof(consumerGroup)} was null")]);
            return Outcome<int>.Failure();
        }

        var result = await _offsetStore.DeleteOffsetsForConsumerAsync(consumerGroup, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            Observe(LogLevel.Error, values: [("Reason", "Underlying offset delete failed"), ("ConsumerGroup", consumerGroup)]);

            return result;
        }

        Observe(LogLevel.Information, values: [("ConsumerGroup", consumerGroup), ("DeletedCount", result.Data)]);

        return result;
    }
}
