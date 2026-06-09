using Atelier.Framework.Attributes;

namespace Atelier.Framework.EventStream.Core;

[Contract("StreamStats", Version = "1.0", Namespace = "Framework.EventStream")]
public class StreamStats
{
    public required string Topic { get; set; }
    public required long TotalEvents { get; set; }
    public required long StartOffset { get; set; }
    public required long EndOffset { get; set; }
    public required long TotalSizeBytes { get; set; }
    public required int SegmentCount { get; set; }
    public DateTime? OldestEventTimestamp { get; set; }
    public DateTime? NewestEventTimestamp { get; set; }
}
