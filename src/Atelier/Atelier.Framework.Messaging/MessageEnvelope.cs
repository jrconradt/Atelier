using Atelier.Framework.Context;
using Atelier.Framework.Context.Extensions;

namespace Atelier.Framework.Messaging;

public class MessageEnvelope<TPayload>
{
    public string MessageId { get; set; } = Guid.NewGuid().ToString();
    public string? Topic { get; set; }
    public TPayload? Payload { get; set; }
    public MessageHeaders Headers { get; set; } = new();
    public MessageRoutingInfo? Routing { get; set; }

    public static MessageEnvelope<TPayload> FromContext(
        IContext context,
        TPayload payload,
        string? topic = null,
        string? targetService = null,
        string? targetDomain = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        var messaging = context.GetMessaging();

        var envelope = new MessageEnvelope<TPayload>
        {
            MessageId = messaging?.MessageId ?? Guid.NewGuid().ToString(),
            Topic = topic ?? messaging?.Topic,
            Payload = payload,
            Headers = context.ToMessageHeaders(),
            Routing = (targetService ?? messaging?.TargetServiceId) != null
                ? new MessageRoutingInfo
                {
                    TargetServiceId = targetService ?? messaging?.TargetServiceId,
                    TargetDomainId = targetDomain ?? messaging?.TargetDomainId,
                    Priority = messaging?.Priority ?? 0,
                    TimeToLiveSeconds = messaging?.TimeToLiveSeconds,
                    DeliveryGuarantee = messaging?.DeliveryGuarantee ?? DeliveryGuarantee.AtLeastOnce
                }
                : null
        };

        return envelope;
    }

    public IContext ToContext()
    {
        var context = ContextMessagingExtensions.FromMessageHeaders(Headers);

        if (Routing != null)
        {
            var messaging = context.GetMessaging();
            if (messaging != null)
            {
                messaging.TargetServiceId = Routing.TargetServiceId;
                messaging.TargetDomainId = Routing.TargetDomainId;
                messaging.Priority = Routing.Priority;
                messaging.TimeToLiveSeconds = Routing.TimeToLiveSeconds;
                messaging.DeliveryGuarantee = Routing.DeliveryGuarantee;
            }
        }

        return context;
    }
}
