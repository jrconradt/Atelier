using Atelier.Framework.EventStream.Configuration;
using Atelier.Framework.EventStream.Consumers;
using Atelier.Framework.EventStream.Core;
using Atelier.Framework.Attributes;
using Atelier.Framework.Outcomes;

namespace Atelier.Framework.EventStream.Orchestration;

public interface IEventStreamManager
{
    public Task<Outcome<IEventStream>> CreateStreamAsync(
        string topic,
        StreamConfiguration config,
        CancellationToken ct);

    public Task<Outcome<IEventStream>> GetStreamAsync(
        string topic,
        CancellationToken ct);

    public Task<Outcome> DeleteStreamAsync(
        string topic,
        CancellationToken ct);

    public Task<Outcome> RegisterConsumerAsync(
        IEventStreamConsumer consumer,
        CancellationToken ct);

    public Task<Outcome> UnregisterConsumerAsync(
        string consumerName,
        CancellationToken ct);

    public Task<Outcome> StartAllConsumersAsync(CancellationToken ct);
    public Task<Outcome> StopAllConsumersAsync(CancellationToken ct);

    public Task<StreamingStats> GetStatsAsync(CancellationToken ct);
}

[Contract("StreamingStats", Version = "1.0", Namespace = "Framework.EventStream")]
public class StreamingStats
{
    public required int ActiveConsumers { get; set; }
    public required long TotalEventsProcessed { get; set; }
    public required long TotalEventsFailed { get; set; }
    public required List<string> StreamNames { get; set; }
}
