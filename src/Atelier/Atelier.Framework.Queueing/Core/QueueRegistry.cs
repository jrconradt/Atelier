using Atelier.Framework.Primitives;
using System.Collections.Concurrent;
using Atelier.Framework.Infrastructure;
using Atelier.Framework.Attributes;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Queueing.Orchestration;
using Atelier.Framework.Requisitions;

namespace Atelier.Framework.Queueing.Core;

public interface IQueueRegistry
{
    Task<Outcome<IQueue>> ResolveAsync(string topic, CancellationToken cancellationToken = default);

    IEnumerable<string> GetTopics();
}

[Infrastructure(InfrastructureLifetime.Singleton)]
public partial class QueueRegistry : IAtelier, IQueueRegistry
{
    [Requisite] protected readonly IQueueManager _queueManager = null!;

    private readonly ConcurrentDictionary<string, IQueue> _queues = new();

    public async Task<Outcome<IQueue>> ResolveAsync(string topic, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", "Topic name was null or empty")]);
            return Outcome<IQueue>.Failure();
        }

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "Topic", topic);

        if (_queues.TryGetValue(topic, out var existingQueue))
        {
            return Outcome<IQueue>.Success(existingQueue);
        }

        var result = await _queueManager.GetQueueAsync(topic, cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            _queues.TryAdd(topic, result.Data!);
        }

        return result;
    }

    public IEnumerable<string> GetTopics()
    {
        return _queues.Keys;
    }
}
