using System.Threading.Channels;
using Atelier.Framework.Outcomes;

namespace Atelier.Framework.Queueing.Core;

public interface IQueue
{
        public string Name { get; }

        public Channel<QueueMessage> Channel { get; }

        public Task<Outcome<QueueMessage>> EnqueueAsync(
        string messageType,
        object payload,
        QueueMessageOptions? options = null,
        CancellationToken cancellationToken = default);

        public Task<QueueStats> GetStatsAsync(CancellationToken cancellationToken = default);

        public Task<Outcome> ClearAsync(CancellationToken cancellationToken = default);
}
