using Atelier.Framework.EventStream.Core;
using Atelier.Framework.Attributes;
using Atelier.Framework.Outcomes;

namespace Atelier.Framework.EventStream.Consumers;

public interface IEventStreamConsumer
{
    public string ConsumerName { get; }
    public string ConsumerGroup { get; }
    public IEnumerable<string> Topics { get; }

    public Task<Outcome> ProcessEventAsync(
        StreamEvent streamEvent,
        CancellationToken cancellationToken);
}

[Contract("ConsumerStats", Version = "1.0", Namespace = "Framework.EventStream")]
public class ConsumerStats
{
    public required string ConsumerName { get; set; }
    public required string ConsumerGroup { get; set; }
    public required long EventsProcessed { get; set; }
    public required long EventsFailed { get; set; }
    public required long CurrentOffset { get; set; }
    public required long Lag { get; set; }
    public required double EventsPerSecond { get; set; }
    public required TimeSpan Uptime { get; set; }
    public required DateTime? LastFailureUtc { get; set; }
}
