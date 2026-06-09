namespace Atelier.Framework.Observability;

public class LogEntry
{
    public required string Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public LogLevel Level { get; set; } = LogLevel.Information;
    public required string Message { get; set; }
    public required string ServiceName { get; set; }
    public string? Exception { get; set; }
    public Dictionary<string, object> Properties { get; set; } = new();
    public string? CorrelationId { get; set; }
    public string? TraceId { get; set; }
    public string? SpanId { get; set; }
    public string Source { get; set; } = string.Empty;
    public Dictionary<string, string> Tags { get; set; } = new();
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
