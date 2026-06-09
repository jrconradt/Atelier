using Atelier.Framework.EventStream.Configuration;
using Atelier.Framework.EventStream.Consumers;
using Atelier.Framework.EventStream.Core;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Testing;
using Microsoft.Extensions.Options;

namespace Atelier.Framework.EventStream;

[TestFixtureRegistry]
public static class EventStreamTestFixtures
{
    [Fixture(typeof(IOptions<EventStreamOptions>))]
    public static IOptions<EventStreamOptions> Options()
    {
        var root = Path.Combine(Path.GetTempPath(), "atelier-test", "eventstream", Guid.NewGuid().ToString("N"));

        return Microsoft.Extensions.Options.Options.Create(new EventStreamOptions
        {
            OffsetStoreDirectory = Path.Combine(root, "offsets"),
            HashRegistryDirectory = Path.Combine(root, "hashes"),
        });
    }

    [Fixture(typeof(IEventStreamConsumer))]
    public static IEventStreamConsumer Consumer()
    {
        return new FixtureConsumer();
    }

    private sealed class FixtureConsumer : IEventStreamConsumer
    {
        public string ConsumerName => "atelier-happy-consumer";

        public string ConsumerGroup => "atelier-happy-group";

        public IEnumerable<string> Topics => Array.Empty<string>();

        public Task<Outcome> ProcessEventAsync(
            StreamEvent streamEvent,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Outcome.Success());
        }
    }
}
