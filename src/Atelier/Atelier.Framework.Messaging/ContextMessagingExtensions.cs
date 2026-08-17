using Atelier.Framework.Context;
using Atelier.Framework.Messaging;
namespace Atelier.Framework.Context.Extensions
{
    public static class ContextMessagingExtensions
    {
        public static IContext AsMessage(
            this IContext context,
            string? topic = null,
            string? targetServiceId = null,
            string? targetDomainId = null)
        {
            var extension = new MessagingContextExtension
            {
                MessageId = Guid.NewGuid().ToString(),
                Topic = topic,
                Timestamp = DateTime.UtcNow,
                SourceServiceId = context.ServiceId,
                SourceDomainId = context.DomainId,
                TargetServiceId = targetServiceId,
                TargetDomainId = targetDomainId
            };

            context.Extensions.Register(extension);
            return context;
        }

        public static IContext WithMessageRouting(
            this IContext context,
            string targetServiceId,
            string? targetDomainId = null,
            int priority = 0,
            int? ttlSeconds = null,
            DeliveryGuarantee guarantee = DeliveryGuarantee.AtLeastOnce)
        {
            var messaging = context.GetMessaging();
            if (messaging == null)
            {
                messaging = new MessagingContextExtension();
                context.Extensions.Register(messaging);
            }

            messaging.TargetServiceId = targetServiceId;
            messaging.TargetDomainId = targetDomainId;
            messaging.Priority = priority;
            messaging.TimeToLiveSeconds = ttlSeconds;
            messaging.DeliveryGuarantee = guarantee;

            return context;
        }

        public static IContext WithMessageHeader(
            this IContext context,
            string key,
            string value)
        {
            var messaging = context.GetMessaging();
            if (messaging == null)
            {
                messaging = new MessagingContextExtension();
                context.Extensions.Register(messaging);
            }

            messaging.SetHeader(key, value);
            return context;
        }

        public static MessagingContextExtension? GetMessaging(this IContext context)
        {
            return context.Extensions.Get<MessagingContextExtension>();
        }

        public static bool IsMessage(this IContext context)
        {
            return context.Extensions.Has<MessagingContextExtension>();
        }

        public static string? GetMessageId(this IContext context)
        {
            return context.GetMessaging()?.MessageId;
        }

        public static string? GetTopic(this IContext context)
        {
            return context.GetMessaging()?.Topic;
        }

        public static string? GetMessageHeader(this IContext context, string key)
        {
            return context.GetMessaging()?.GetHeader(key);
        }

        public static IReadOnlyDictionary<string, string> GetAllMessageHeaders(this IContext context)
        {
            return context.GetMessaging()?.CustomHeaders ?? new Dictionary<string, string>();
        }

        public static MessageHeaders ToMessageHeaders(this IContext context)
        {
            var messaging = context.GetMessaging();

            var headers = new MessageHeaders
            {
                CorrelationId = context.CorrelationId,
                TraceId = context.TraceId,
                SpanId = context.SpanId,
                ParentSpanId = context.ParentSpanId,
                SourceServiceId = messaging?.SourceServiceId ?? context.ServiceId,
                SourceDomainId = messaging?.SourceDomainId ?? context.DomainId,
                ContextId = context.ContextId,
                ContextName = context.Name,
                ContextScope = context.Scope.ToString(),
                ContextLifecycle = context.Lifecycle.ToString()
            };

            if (messaging != null)
            {
                foreach (var kvp in messaging.CustomHeaders)
                {
                    headers.SetHeader(kvp.Key, kvp.Value);
                }
            }

            return headers;
        }

        public static IContext FromMessageHeaders(MessageHeaders headers)
        {
            var context = new global::Atelier.Framework.Context.Context(
                headers.ContextId ?? Guid.NewGuid().ToString(),
                headers.ContextName ?? "MessageContext",
                null
            );

            context.CorrelationId = headers.CorrelationId;
            context.TraceId = headers.TraceId;
            context.SpanId = headers.SpanId;
            context.ParentSpanId = headers.ParentSpanId;
            context.ServiceId = headers.SourceServiceId;
            context.DomainId = headers.SourceDomainId;

            var messaging = new MessagingContextExtension
            {
                SourceServiceId = headers.SourceServiceId,
                SourceDomainId = headers.SourceDomainId
            };

            foreach (var kvp in headers.CustomHeaders)
            {
                messaging.SetHeader(kvp.Key, kvp.Value);
            }

            context.Extensions.Register(messaging);

            return context;
        }
    }
}
