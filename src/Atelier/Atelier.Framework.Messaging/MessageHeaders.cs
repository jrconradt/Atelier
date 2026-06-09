using Atelier.Framework.Context;

namespace Atelier.Framework.Messaging;

public class MessageHeaders
{
    private readonly Dictionary<string, string> _headers = new();

    public string? CorrelationId { get; set; }

    public string? TraceId { get; set; }

    public string? SpanId { get; set; }

    public string? ParentSpanId { get; set; }

    public string? SourceService { get; set; }

    public string? SourceServiceId { get; set; }

    public string? SourceDomainId { get; set; }

    public string? TargetService { get; set; }

    public string? ContextId { get; set; }

    public string? ContextName { get; set; }

    public string? ContextScope { get; set; }

    public string? ContextLifecycle { get; set; }

    public Dictionary<string, string> CustomHeaders { get; set; } = new();

    public int Priority { get; set; } = 0;

    public int? TimeToLiveSeconds { get; set; }

    public string? MessageVersion { get; set; }

    public string? ContentType { get; set; }

    public string? ContentEncoding { get; set; }

    public IContext? Context { get; set; }

    public Dictionary<string, object> Metadata { get; set; } = new();

    public IReadOnlyDictionary<string, string> Headers => _headers;

    public MessageHeaders()
    {
    }

    public MessageHeaders(IDictionary<string, string> headers)
    {
        foreach (var kvp in headers)
        {
            _headers[kvp.Key] = kvp.Value;
        }
    }

    public void SetHeader(string key, string value)
    {
        _headers[key] = value;
    }

    public string? GetHeader(string key)
    {
        return _headers.TryGetValue(key, out var value) ? value : null;
    }
}
