using Atelier.Framework.EventStream.Consumers.Services;
using Atelier.Framework.Outcomes;
using System.Collections.Concurrent;

namespace Atelier.Host.{{ boutiqueName }};

public class NoOpOffsetManagerService : IOffsetManagerService
{
    private readonly ConcurrentDictionary<string, long> _offsets = new();

    public Task<Outcome<long>> GetStartingOffsetAsync(string consumerGroup, string topic, CancellationToken cancellationToken = default)
    {
        var key = $"{consumerGroup}:{topic}";
        var offset = _offsets.GetOrAdd(key, 0L);
        return Task.FromResult(Outcome<long>.Success(offset));
    }

    public Task<Outcome> CommitOffsetAsync(string consumerGroup, string topic, long offset, CancellationToken cancellationToken = default)
    {
        var key = $"{consumerGroup}:{topic}";
        _offsets.AddOrUpdate(key, offset, (_, _) => offset);
        return Task.FromResult(Outcome.Success());
    }

    public Task<Outcome> CommitOffsetsAsync(string consumerGroup, Dictionary<string, long> topicOffsets, CancellationToken cancellationToken = default)
    {
        foreach (var (topic, offset) in topicOffsets)
        {
            var key = $"{consumerGroup}:{topic}";
            _offsets.AddOrUpdate(key, offset, (_, _) => offset);
        }
        return Task.FromResult(Outcome.Success());
    }

    public Task<Outcome<int>> DeleteOffsetsForConsumerAsync(string consumerGroup, CancellationToken cancellationToken = default)
    {
        var prefix = $"{consumerGroup}:";
        var removed = 0;
        foreach (var key in _offsets.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal)
                && _offsets.TryRemove(key, out _))
            {
                removed++;
            }
        }
        return Task.FromResult(Outcome<int>.Success(removed));
    }
}
