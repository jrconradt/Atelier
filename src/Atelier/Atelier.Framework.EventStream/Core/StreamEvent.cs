using Atelier.Framework.Attributes;

namespace Atelier.Framework.EventStream.Core;

[Contract("StreamEvent", Version = "1.0", Namespace = "Framework.EventStream")]
public class StreamEvent
{
    public required string Topic { get; set; }
    public required long Offset { get; set; }
    public required DateTime Timestamp { get; set; }
    public required byte[] Payload { get; set; }
    public EventMetadata? Metadata { get; set; }
}
