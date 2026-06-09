using Atelier.Framework.Context;

namespace Atelier.Framework.Network.Transport;

internal static class ContextHeaderInjector
{
    public const string TRACEPARENT_HEADER_NAME = "traceparent";

    public static void Apply(HttpRequestMessage request, IContextAccessor? contextAccessor)
    {
        ArgumentNullException.ThrowIfNull(request);

        var current = CurrentContext(contextAccessor);
        if (current == null)
        {
            return;
        }

        var encoded = WireContextCodec.Encode(current);
        if (!string.IsNullOrEmpty(encoded))
        {
            request.Headers.TryAddWithoutValidation(WireContextCodec.CANONICAL_HEADER_NAME, encoded);
        }

        var traceParent = current.BuildTraceParent();
        if (!string.IsNullOrEmpty(traceParent))
        {
            request.Headers.TryAddWithoutValidation(TRACEPARENT_HEADER_NAME, traceParent);
        }
    }

    public static void Stamp(IDictionary<string, string> headers, IContextAccessor? contextAccessor)
    {
        ArgumentNullException.ThrowIfNull(headers);

        var current = CurrentContext(contextAccessor);
        if (current == null)
        {
            return;
        }

        var encoded = WireContextCodec.Encode(current);
        if (!string.IsNullOrEmpty(encoded))
        {
            headers[WireContextCodec.LOWERCASE_HEADER_NAME] = encoded;
        }

        var traceParent = current.BuildTraceParent();
        if (!string.IsNullOrEmpty(traceParent))
        {
            headers[TRACEPARENT_HEADER_NAME] = traceParent;
        }
    }

    private static IContext? CurrentContext(IContextAccessor? contextAccessor)
    {
        if (contextAccessor == null)
        {
            return null;
        }

        var current = contextAccessor.Current;
        if (current == null
            || !HasPropagatablePayload(current))
        {
            return null;
        }

        return current;
    }

    private static bool HasPropagatablePayload(IContext context)
    {
        if (!string.IsNullOrEmpty(context.TraceId)
            || !string.IsNullOrEmpty(context.SpanId)
            || !string.IsNullOrEmpty(context.CorrelationId))
        {
            return true;
        }

        var authorization = context.Authorization;
        if (authorization == null)
        {
            return false;
        }

        return !string.IsNullOrEmpty(authorization.UserId)
            || !string.IsNullOrEmpty(authorization.TenantId)
            || !string.IsNullOrEmpty(authorization.SessionId)
            || authorization.Claims.Count > 0
            || authorization.Roles.Count > 0
            || authorization.Permissions.Count > 0;
    }
}
