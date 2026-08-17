using Atelier.Framework.Context;

namespace Atelier.Framework.Network.Transport;

internal static class ContextHeaderInjector
{
    public const string TRACEPARENT_HEADER_NAME = "traceparent";

    public static void Apply(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var current = CurrentContext();
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

    public static void Stamp(IDictionary<string, string> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        var current = CurrentContext();
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

    private static IContext? CurrentContext()
    {
        var current = AmbientContext.Current;
        if (!HasPropagatablePayload(current))
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
