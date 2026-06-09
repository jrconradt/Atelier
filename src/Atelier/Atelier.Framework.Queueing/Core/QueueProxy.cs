using Atelier.Framework.Context;
using Atelier.Framework.Outcomes;

namespace Atelier.Framework.Queueing.Core;

public readonly struct QueueProxy
{
    private readonly IQueueRegistry _registry;
    private readonly string _topic;
    private readonly IContextAccessor? _contextAccessor;

    internal QueueProxy(IQueueRegistry registry, string topic, IContextAccessor? contextAccessor = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _topic = topic ?? throw new ArgumentNullException(nameof(topic));
        _contextAccessor = contextAccessor;
    }

    public string Topic => _topic;

    private QueueMessageOptions PropagateTrace(QueueMessageOptions? options)
    {
        var resolved = options ?? new QueueMessageOptions();

        var current = _contextAccessor?.Current;
        if (current == null
            || string.IsNullOrEmpty(current.TraceId))
        {
            return resolved;
        }

        resolved.TraceId ??= current.TraceId;
        resolved.ParentSpanId ??= current.SpanId;
        resolved.SpanId ??= TracingContext.GenerateSpanId();
        resolved.CorrelationId ??= current.CorrelationId;
        return resolved;
    }

    public async Task<Outcome> StreamAsync(
        object payload,
        CancellationToken cancellationToken = default)
    {
        var resolved = await _registry.ResolveAsync(_topic, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess)
        {
            return Outcome.Failure();
        }

        var result = await resolved.Data.EnqueueAsync(
            "StreamEvent",
            payload,
            PropagateTrace(null),
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Outcome.Success()
            : Outcome.Failure();
    }

    public async Task<Outcome> StreamAsync<T>(
        T payload,
        CancellationToken cancellationToken = default)
    {
        var resolved = await _registry.ResolveAsync(_topic, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess)
        {
            return Outcome.Failure();
        }

        var result = await resolved.Data.EnqueueAsync(
            typeof(T).Name,
            payload!,
            PropagateTrace(null),
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Outcome.Success()
            : Outcome.Failure();
    }

    public async Task<Outcome> StreamAsync<T>(
        T payload,
        QueueMessageOptions options,
        CancellationToken cancellationToken = default)
    {
        var resolved = await _registry.ResolveAsync(_topic, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess)
        {
            return Outcome.Failure();
        }

        var result = await resolved.Data.EnqueueAsync(
            typeof(T).Name,
            payload!,
            PropagateTrace(options),
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Outcome.Success()
            : Outcome.Failure();
    }

    public async Task<Outcome> StreamAsAsync(
        string messageType,
        object payload,
        QueueMessageOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var resolved = await _registry.ResolveAsync(_topic, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess)
        {
            return Outcome.Failure();
        }

        var result = await resolved.Data.EnqueueAsync(
            messageType,
            payload,
            PropagateTrace(options),
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Outcome.Success()
            : Outcome.Failure();
    }

    public async Task<QueueStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var resolved = await _registry.ResolveAsync(_topic, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess)
        {
            throw new InvalidOperationException($"Failed to resolve queue '{_topic}'");
        }

        return await resolved.Data.GetStatsAsync(cancellationToken).ConfigureAwait(false);
    }

    public override string ToString() => $"Queue[{_topic}]";
}
