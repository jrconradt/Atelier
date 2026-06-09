namespace Atelier.Framework.Attache.Audit;

public sealed record CapabilityAuditChainVerification
{
    public required bool IsIntact { get; init; }
    public required long VerifiedEntryCount { get; init; }
    public long? FirstBreakSequence { get; init; }
    public string? FirstBreakReason { get; init; }
    public required string AnchorHash { get; init; }
    public required long AnchorSequence { get; init; }
}
