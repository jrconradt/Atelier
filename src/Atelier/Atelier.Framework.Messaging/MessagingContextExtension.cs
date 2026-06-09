using Atelier.Framework.Messaging;

namespace Atelier.Framework.Context.Extensions
{
    public class MessagingContextExtension : IContextExtension
    {
        public string ExtensionName => "Messaging";

        public bool ShouldPropagateToChildren => false;

        public string? MessageId { get; set; }
        public string? Topic { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? SourceServiceId { get; set; }
        public string? SourceDomainId { get; set; }
        public string? TargetServiceId { get; set; }
        public string? TargetDomainId { get; set; }
        public int Priority { get; set; }
        public int? TimeToLiveSeconds { get; set; }
        public DeliveryGuarantee DeliveryGuarantee { get; set; } = DeliveryGuarantee.AtLeastOnce;

        private readonly Dictionary<string, string> _customHeaders = new();
        public IReadOnlyDictionary<string, string> CustomHeaders => _customHeaders;

        public void SetHeader(string key, string value)
        {
            _customHeaders[key] = value;
        }

        public string? GetHeader(string key)
        {
            return _customHeaders.TryGetValue(key, out var value) ? value : null;
        }

        public IContextExtension Clone()
        {
            var clone = new MessagingContextExtension
            {
                MessageId = MessageId,
                Topic = Topic,
                Timestamp = Timestamp,
                SourceServiceId = SourceServiceId,
                SourceDomainId = SourceDomainId,
                TargetServiceId = TargetServiceId,
                TargetDomainId = TargetDomainId,
                Priority = Priority,
                TimeToLiveSeconds = TimeToLiveSeconds,
                DeliveryGuarantee = DeliveryGuarantee
            };

            foreach (var kvp in _customHeaders)
            {
                clone._customHeaders[kvp.Key] = kvp.Value;
            }

            return clone;
        }
    }
}
