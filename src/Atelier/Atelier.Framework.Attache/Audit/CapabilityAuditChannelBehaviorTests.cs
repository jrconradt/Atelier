using Atelier.Framework.Testing;

namespace Atelier.Framework.Attache.Audit;

public static class CapabilityAuditChannelBehaviorTests
{
    private const string AUDIT_PATH_ENV = "ATELIER_AUDIT_LOG_PATH";
    private const string AUDIT_HMAC_KEY_ENV = "ATELIER_AUDIT_HMAC_KEY";

    static CapabilityAuditChannelBehaviorTests()
    {
        Environment.SetEnvironmentVariable(AUDIT_HMAC_KEY_ENV, "atelier-test-audit-hmac-key");
    }

    private static CapabilityAuditChannel NewChannel(out string? priorPath, out string path)
    {
        priorPath = Environment.GetEnvironmentVariable(AUDIT_PATH_ENV);
        path = Path.Combine(Path.GetTempPath(), $"atelier-audit-{Guid.NewGuid():N}.jsonl");
        Environment.SetEnvironmentVariable(AUDIT_PATH_ENV, path);
        return new CapabilityAuditChannel(null);
    }

    private static void ReleaseChannel(string? priorPath, string path)
    {
        Environment.SetEnvironmentVariable(AUDIT_PATH_ENV, priorPath);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static AuditPrincipal Principal()
    {
        return new AuditPrincipal
        {
            UserId = "user-1",
            TenantId = "tenant-1",
            SessionId = "session-1",
            IsAuthenticated = true
        };
    }

    [GeneratedTest("Attache/Audit-Grant-Records-Granted-Decision-And-Ticket", "global::Atelier.Framework.Attache.Audit.CapabilityAuditChannel")]
    public static void GrantRecordsGrantedDecisionWithSuppliedFields()
    {
        var channel = NewChannel(out var priorPath, out var path);

        try
        {
            var entry = channel.RecordGrant(
                Principal(),
                "consumer-a",
                "Cache.Read",
                "ticket-7");

            if (entry.Decision != CapabilityAuditDecision.Granted)
            {
                throw new InvalidOperationException($"expected Granted, got {entry.Decision}");
            }
            if (entry.Sequence != 1)
            {
                throw new InvalidOperationException($"expected first sequence to be 1, got {entry.Sequence}");
            }
            if (entry.ConsumerId != "consumer-a"
                || entry.CapabilityName != "Cache.Read"
                || entry.TicketId != "ticket-7")
            {
                throw new InvalidOperationException("grant entry did not carry the supplied consumer/capability/ticket");
            }
            if (entry.OutcomeCode != "GRANTED")
            {
                throw new InvalidOperationException($"expected GRANTED outcome code, got '{entry.OutcomeCode}'");
            }
            if (entry.PreviousHash != string.Empty)
            {
                throw new InvalidOperationException($"expected genesis previous-hash to be empty, got '{entry.PreviousHash}'");
            }
            if (string.IsNullOrEmpty(entry.EntryHash))
            {
                throw new InvalidOperationException("grant entry produced no entry hash");
            }
        }
        finally
        {
            ReleaseChannel(priorPath, path);
        }
    }

    [GeneratedTest("Attache/Audit-Denial-Records-Reason-And-Outcome-Code", "global::Atelier.Framework.Attache.Audit.CapabilityAuditChannel")]
    public static void DenialRecordsDeniedDecisionWithReason()
    {
        var channel = NewChannel(out var priorPath, out var path);

        try
        {
            var entry = channel.RecordDenial(
                Principal(),
                "consumer-b",
                "Cache.Write",
                "FORBIDDEN",
                "no grant on file");

            if (entry.Decision != CapabilityAuditDecision.Denied)
            {
                throw new InvalidOperationException($"expected Denied, got {entry.Decision}");
            }
            if (entry.OutcomeCode != "FORBIDDEN")
            {
                throw new InvalidOperationException($"expected FORBIDDEN outcome code, got '{entry.OutcomeCode}'");
            }
            if (entry.Reason != "no grant on file")
            {
                throw new InvalidOperationException($"expected denial reason to be retained, got '{entry.Reason}'");
            }
            if (entry.TicketId is not null)
            {
                throw new InvalidOperationException($"expected denial to carry no ticket, got '{entry.TicketId}'");
            }
        }
        finally
        {
            ReleaseChannel(priorPath, path);
        }
    }

    [GeneratedTest("Attache/Audit-Release-Records-Released-Decision", "global::Atelier.Framework.Attache.Audit.CapabilityAuditChannel")]
    public static void ReleaseRecordsReleasedDecisionWithTicketAndOutcome()
    {
        var channel = NewChannel(out var priorPath, out var path);

        try
        {
            var entry = channel.RecordRelease(
                Principal(),
                "consumer-c",
                "Cache.Read",
                "ticket-9",
                "RELEASED");

            if (entry.Decision != CapabilityAuditDecision.Released)
            {
                throw new InvalidOperationException($"expected Released, got {entry.Decision}");
            }
            if (entry.TicketId != "ticket-9")
            {
                throw new InvalidOperationException($"expected ticket-9, got '{entry.TicketId}'");
            }
            if (entry.OutcomeCode != "RELEASED")
            {
                throw new InvalidOperationException($"expected RELEASED outcome code, got '{entry.OutcomeCode}'");
            }
        }
        finally
        {
            ReleaseChannel(priorPath, path);
        }
    }

    [GeneratedTest("Attache/Audit-Sequence-Increments-And-Chain-Links", "global::Atelier.Framework.Attache.Audit.CapabilityAuditChannel")]
    public static void AppendsIncrementSequenceAndLinkEachEntryToPriorHash()
    {
        var channel = NewChannel(out var priorPath, out var path);

        try
        {
            var first = channel.RecordGrant(
                Principal(),
                "consumer-a",
                "Cache.Read",
                "ticket-1");
            var second = channel.RecordDenial(
                Principal(),
                "consumer-a",
                "Cache.Write",
                "FORBIDDEN",
                "denied");
            var third = channel.RecordRelease(
                Principal(),
                "consumer-a",
                "Cache.Read",
                "ticket-1",
                "RELEASED");

            if (first.Sequence != 1
                || second.Sequence != 2
                || third.Sequence != 3)
            {
                throw new InvalidOperationException($"expected sequences 1,2,3, got {first.Sequence},{second.Sequence},{third.Sequence}");
            }
            if (second.PreviousHash != first.EntryHash)
            {
                throw new InvalidOperationException("second entry's previous-hash does not link to the first entry's hash");
            }
            if (third.PreviousHash != second.EntryHash)
            {
                throw new InvalidOperationException("third entry's previous-hash does not link to the second entry's hash");
            }
        }
        finally
        {
            ReleaseChannel(priorPath, path);
        }
    }

    [GeneratedTest("Attache/Audit-Snapshot-Returns-Recorded-Entries-In-Order", "global::Atelier.Framework.Attache.Audit.CapabilityAuditChannel")]
    public static void SnapshotReturnsEveryRecordedEntryInAppendOrder()
    {
        var channel = NewChannel(out var priorPath, out var path);

        try
        {
            channel.RecordGrant(
                Principal(),
                "consumer-a",
                "Cache.Read",
                "ticket-1");
            channel.RecordDenial(
                Principal(),
                "consumer-b",
                "Cache.Write",
                "FORBIDDEN",
                "denied");

            var snapshot = channel.Snapshot();
            if (snapshot.Count != 2)
            {
                throw new InvalidOperationException($"expected 2 entries in snapshot, got {snapshot.Count}");
            }
            if (snapshot[0].Sequence != 1
                || snapshot[1].Sequence != 2)
            {
                throw new InvalidOperationException($"snapshot out of order: {snapshot[0].Sequence},{snapshot[1].Sequence}");
            }
            if (snapshot[0].Decision != CapabilityAuditDecision.Granted
                || snapshot[1].Decision != CapabilityAuditDecision.Denied)
            {
                throw new InvalidOperationException("snapshot decisions did not match the recorded order");
            }
        }
        finally
        {
            ReleaseChannel(priorPath, path);
        }
    }

    [GeneratedTest("Attache/Audit-Verify-Chain-Intact-Over-Recorded-Entries", "global::Atelier.Framework.Attache.Audit.CapabilityAuditChannel")]
    public static void VerifyChainReportsIntactForRecordedEntries()
    {
        var channel = NewChannel(out var priorPath, out var path);

        try
        {
            channel.RecordGrant(
                Principal(),
                "consumer-a",
                "Cache.Read",
                "ticket-1");
            channel.RecordRelease(
                Principal(),
                "consumer-a",
                "Cache.Read",
                "ticket-1",
                "RELEASED");

            var verification = channel.VerifyChain();
            if (!verification.IsIntact)
            {
                throw new InvalidOperationException($"expected intact chain, break at {verification.FirstBreakSequence}: {verification.FirstBreakReason}");
            }
            if (verification.VerifiedEntryCount != 2)
            {
                throw new InvalidOperationException($"expected 2 verified entries, got {verification.VerifiedEntryCount}");
            }
            if (verification.FirstBreakSequence is not null
                || verification.FirstBreakReason is not null)
            {
                throw new InvalidOperationException("intact verification still reported a break");
            }
        }
        finally
        {
            ReleaseChannel(priorPath, path);
        }
    }

    [GeneratedTest("Attache/Audit-Verify-Empty-Chain-Is-Intact-At-Genesis", "global::Atelier.Framework.Attache.Audit.CapabilityAuditChannel")]
    public static void VerifyChainOnEmptyChannelIsIntactAtGenesis()
    {
        var channel = NewChannel(out var priorPath, out var path);

        try
        {
            var verification = channel.VerifyChain();
            if (!verification.IsIntact)
            {
                throw new InvalidOperationException("empty audit channel should verify as intact");
            }
            if (verification.VerifiedEntryCount != 0)
            {
                throw new InvalidOperationException($"empty channel should verify 0 entries, got {verification.VerifiedEntryCount}");
            }
            if (verification.AnchorSequence != 0
                || verification.AnchorHash != string.Empty)
            {
                throw new InvalidOperationException($"empty channel should anchor at genesis, got seq {verification.AnchorSequence} hash '{verification.AnchorHash}'");
            }
        }
        finally
        {
            ReleaseChannel(priorPath, path);
        }
    }
}
