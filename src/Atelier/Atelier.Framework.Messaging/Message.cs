using Atelier.Framework.Attributes;

namespace Atelier.Framework.Messaging;

[ContractAttribute("Message", Version = "1.0", Namespace = "Framework.Messaging")]
public class Message : IMessage
{
    public string? Id { get; set; }
    public string? Topic { get; set; }
    public MessageHeaders Headers { get; set; } = new();
    public DateTime Timestamp { get; set; }
    public string? CorrelationId { get; set; }
}
