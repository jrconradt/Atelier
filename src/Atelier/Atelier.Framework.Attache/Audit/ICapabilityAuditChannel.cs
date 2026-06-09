namespace Atelier.Framework.Attache.Audit;

public interface ICapabilityAuditChannel
{
    public CapabilityAuditEntry RecordGrant(
        AuditPrincipal principal,
        string consumerId,
        string capabilityName,
        string ticketId);

    public CapabilityAuditEntry RecordDenial(
        AuditPrincipal principal,
        string consumerId,
        string capabilityName,
        string outcomeCode,
        string reason);

    public CapabilityAuditEntry RecordRelease(
        AuditPrincipal principal,
        string consumerId,
        string capabilityName,
        string ticketId,
        string outcomeCode);

    public IReadOnlyList<CapabilityAuditEntry> Snapshot();

    public CapabilityAuditChainVerification VerifyChain();
}
