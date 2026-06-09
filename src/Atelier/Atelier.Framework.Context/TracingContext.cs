using System.Security.Cryptography;

using Atelier.Framework.Context;
namespace Atelier.Framework.Context
{
    public static class TracingContext
    {
        private const string TRACEPARENT_VERSION = "00";
        private const string TRACEPARENT_SAMPLED_FLAGS = "01";
        private const int TRACE_ID_HEX_LENGTH = 32;
        private const int SPAN_ID_HEX_LENGTH = 16;

        private static readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();

        public static string GenerateTraceId()
        {
            var bytes = new byte[16];
            _rng.GetBytes(bytes);
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        public static string GenerateSpanId()
        {
            var bytes = new byte[8];
            _rng.GetBytes(bytes);
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        public static void InitializeTracing(this IContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (string.IsNullOrEmpty(context.TraceId))
            {
                context.TraceId = GenerateTraceId();
                context.SpanId = GenerateSpanId();
                context.ParentSpanId = null;
            }
            else
            {
                context.ParentSpanId = context.SpanId;
                context.SpanId = GenerateSpanId();
            }

            if (string.IsNullOrEmpty(context.CorrelationId))
            {
                context.CorrelationId = context.TraceId;
            }
        }

        public static void AdoptParentSpan(
            this IContext context,
            string? traceId,
            string? parentSpanId,
            string? correlationId)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            context.TraceId = string.IsNullOrEmpty(traceId) ? GenerateTraceId() : traceId;
            context.ParentSpanId = string.IsNullOrEmpty(parentSpanId) ? null : parentSpanId;
            context.SpanId = GenerateSpanId();
            context.CorrelationId = string.IsNullOrEmpty(correlationId) ? context.TraceId : correlationId;
        }

        public static string? BuildTraceParent(this IContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (string.IsNullOrEmpty(context.TraceId)
                || string.IsNullOrEmpty(context.SpanId))
            {
                return null;
            }

            var traceId = NormalizeHex(context.TraceId, TRACE_ID_HEX_LENGTH);
            var spanId = NormalizeHex(context.SpanId, SPAN_ID_HEX_LENGTH);
            if (traceId == null
                || spanId == null)
            {
                return null;
            }

            return $"{TRACEPARENT_VERSION}-{traceId}-{spanId}-{TRACEPARENT_SAMPLED_FLAGS}";
        }

        public static bool TryParseTraceParent(
            string? traceParent,
            out string traceId,
            out string parentSpanId)
        {
            traceId = string.Empty;
            parentSpanId = string.Empty;

            if (string.IsNullOrWhiteSpace(traceParent))
            {
                return false;
            }

            var fields = traceParent.Split('-');
            if (fields.Length != 4)
            {
                return false;
            }

            var candidateTrace = NormalizeHex(fields[1], TRACE_ID_HEX_LENGTH);
            var candidateSpan = NormalizeHex(fields[2], SPAN_ID_HEX_LENGTH);
            if (candidateTrace == null
                || candidateSpan == null)
            {
                return false;
            }

            traceId = candidateTrace;
            parentSpanId = candidateSpan;
            return true;
        }

        private static string? NormalizeHex(string value, int expectedLength)
        {
            if (value.Length != expectedLength)
            {
                return null;
            }

            var lowered = value.ToLowerInvariant();
            foreach (var c in lowered)
            {
                var isHex = (c >= '0' && c <= '9')
                    || (c >= 'a' && c <= 'f');
                if (!isHex)
                {
                    return null;
                }
            }

            return lowered;
        }

        public static IContext CreateChildWithTracing(this IContext parent, string name, ContextScope scope = ContextScope.Operation)
        {
            var child = parent.CreateChild(name, scope);
            child.InitializeTracing();
            return child;
        }
    }
}
