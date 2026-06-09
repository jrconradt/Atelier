namespace Atelier.Framework.Attache.Audit;

public enum CapabilityAuditDecision
{
    Granted,
    Denied,
    Released
}

public sealed record CapabilityAuditEntry
{
    public required long Sequence { get; init; }
    public required DateTime Timestamp { get; init; }
    public required CapabilityAuditDecision Decision { get; init; }
    public required string ConsumerId { get; init; }
    public required string CapabilityName { get; init; }
    public string? TicketId { get; init; }
    public string? OutcomeCode { get; init; }
    public string? Reason { get; init; }
    public string? PrincipalUserId { get; init; }
    public string? PrincipalTenantId { get; init; }
    public string? PrincipalSessionId { get; init; }
    public bool PrincipalIsAuthenticated { get; init; }
    public string PreviousHash { get; init; } = string.Empty;
    public string EntryHash { get; init; } = string.Empty;
}
