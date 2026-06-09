using Atelier.Framework.Attributes;

namespace Atelier.Framework.EventStream.Core;

[Contract("EventMetadata", Version = "1.0", Namespace = "Framework.EventStream")]
public class EventMetadata
{
    public const int CURRENT_SCHEMA_VERSION = 1;

    public int SchemaVersion { get; set; } = CURRENT_SCHEMA_VERSION;
    public string? CorrelationId { get; set; }
    public string? TraceId { get; set; }
    public string? SpanId { get; set; }
    public string? ParentSpanId { get; set; }
    public string? Source { get; set; }
    public string? EventType { get; set; }
    public DateTime Timestamp { get; set; }
    public Dictionary<string, string>? CustomProperties { get; set; }
}
