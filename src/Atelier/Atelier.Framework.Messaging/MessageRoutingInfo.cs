using Atelier.Framework.Attributes;

namespace Atelier.Framework.Messaging
{
        [ContractAttribute("MessageRoutingInfo", Version = "1.0", Namespace = "Framework.Messaging")]
    public class MessageRoutingInfo
    {
                public string? TargetServiceId { get; set; }

                public string? TargetDomainId { get; set; }

                public int Priority { get; set; }

                public int? TimeToLiveSeconds { get; set; }

                public DeliveryGuarantee DeliveryGuarantee { get; set; } = DeliveryGuarantee.AtLeastOnce;
    }
}
