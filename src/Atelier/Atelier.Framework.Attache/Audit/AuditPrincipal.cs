namespace Atelier.Framework.Attache.Audit;

public sealed record AuditPrincipal
{
    public string? UserId { get; init; }
    public string? TenantId { get; init; }
    public string? SessionId { get; init; }
    public bool IsAuthenticated { get; init; }

    public static AuditPrincipal Anonymous { get; } = new();
}
