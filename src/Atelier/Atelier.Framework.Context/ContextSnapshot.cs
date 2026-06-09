
namespace Atelier.Framework.Context
{
    public class ContextSnapshot
    {
        public string ContextId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public ContextScope Scope { get; set; }
        public ContextLifecycle Lifecycle { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string? ServiceId { get; set; }
        public string? DomainId { get; set; }
        public string? CorrelationId { get; set; }
        public string? TraceId { get; set; }
        public string? SpanId { get; set; }
        public string? ParentSpanId { get; set; }
        public Dictionary<string, string> ServiceMetadata { get; set; } = new();
        public ContextStatus Status { get; set; }
        public Dictionary<string, object> Results { get; set; } = new();
        public Dictionary<string, string> Data { get; set; } = new();
        public Dictionary<string, string> AdditionalData { get; set; } = new();
        public DateTime SnapshotTakenAt { get; set; } = DateTime.UtcNow;
    }
}
