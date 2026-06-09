namespace Atelier.Framework.Context;

public class ContextEnvelope
{
    public int Version { get; set; }
    public string ContextId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ContextScope Scope { get; set; }
    public ContextLifecycle Lifecycle { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? ServiceId { get; set; }
    public string? DomainId { get; set; }
    public string? CorrelationId { get; set; }
    public string? TraceId { get; set; }
    public string? SpanId { get; set; }
    public string? ParentSpanId { get; set; }
    public ContextStatus Status { get; set; }
    public bool IsCompileTime { get; set; }
    public bool IsRuntime { get; set; }
    public Dictionary<string, string>? Data { get; set; }
    public Dictionary<string, object>? Results { get; set; }
    public AuthorizationSummary? Authorization { get; set; }
    public ScopeLimiterSummary? ScopeLimiter { get; set; }
}
