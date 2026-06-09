namespace Atelier.Framework.Messaging;

public interface IMessage
{
    public string? Id { get; set; }
    public string? Topic { get; set; }
    public MessageHeaders Headers { get; set; }
    public DateTime Timestamp { get; set; }
    public string? CorrelationId { get; set; }
}
