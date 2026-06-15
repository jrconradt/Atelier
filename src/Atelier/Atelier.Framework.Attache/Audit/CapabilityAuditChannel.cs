using Atelier.Framework.Primitives;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Atelier.Framework.Attributes;
using Atelier.Framework.Observability;

namespace Atelier.Framework.Attache.Audit;

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class CapabilityAuditChannel : IAtelier, ICapabilityAuditChannel, IAsyncDisposable
{
    private const string AuditChannelName = "Atelier.Capability.Audit";
    private const int MaxRetainedEntries = 100_000;
    private const string GenesisHash = "";
    private const string AUDIT_PATH_ENV = "ATELIER_AUDIT_LOG_PATH";
    private const string DEFAULT_AUDIT_FILE = "atelier-capability-audit.jsonl";
    private const string AUDIT_HMAC_KEY_ENV = "ATELIER_AUDIT_HMAC_KEY";

    private readonly ConcurrentQueue<CapabilityAuditEntry> _entries = new();
    private readonly ChainStateHolder _chain = new();
    private readonly CheckpointHolder _checkpoint = new();
    private readonly string _durablePath = ResolveDurablePath();
    private readonly Channel<CapabilityAuditEntry> _durableSink = Channel.CreateUnbounded<CapabilityAuditEntry>(
        new UnboundedChannelOptions
        {
            SingleReader = true
        });

    private static readonly JsonSerializerOptions DurableSerialization = new()
    {
        WriteIndented = false
    };

    private readonly byte[]? _auditHmacKey = ResolveHmacKey();

    private readonly StrongBox<Task?> _durableWriter = new();
    private readonly TaskCompletionSource _writerClaim = new();

    private void EnsureDurableWriter()
    {
        if (Volatile.Read(ref _durableWriter.Value) is not null)
        {
            return;
        }

        if (!_writerClaim.TrySetResult())
        {
            return;
        }

        Volatile.Write(ref _durableWriter.Value, Task.Run(DrainDurableSinkAsync));
    }

    private sealed class ChainStateHolder
    {
        public ChainState Current = new(0, GenesisHash);
    }

    private sealed class ChainState
    {
        public ChainState(long sequence, string lastHash)
        {
            Sequence = sequence;
            LastHash = lastHash;
        }

        public readonly long Sequence;
        public readonly string LastHash;
    }

    private sealed class CheckpointHolder
    {
        public Checkpoint Current = new(0, GenesisHash);
    }

    private sealed class Checkpoint
    {
        public Checkpoint(long sequence, string hash)
        {
            Sequence = sequence;
            Hash = hash;
        }

        public readonly long Sequence;
        public readonly string Hash;
    }

    public CapabilityAuditEntry RecordGrant(
        AuditPrincipal principal,
        string consumerId,
        string capabilityName,
        string ticketId)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return Append(
            CapabilityAuditDecision.Granted,
            consumerId,
            capabilityName,
            ticketId,
            "GRANTED",
            null,
            principal);
    }

    public CapabilityAuditEntry RecordDenial(
        AuditPrincipal principal,
        string consumerId,
        string capabilityName,
        string outcomeCode,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return Append(
            CapabilityAuditDecision.Denied,
            consumerId,
            capabilityName,
            null,
            outcomeCode,
            reason,
            principal);
    }

    public CapabilityAuditEntry RecordRelease(
        AuditPrincipal principal,
        string consumerId,
        string capabilityName,
        string ticketId,
        string outcomeCode)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return Append(
            CapabilityAuditDecision.Released,
            consumerId,
            capabilityName,
            ticketId,
            outcomeCode,
            null,
            principal);
    }

    public IReadOnlyList<CapabilityAuditEntry> Snapshot()
    {
        return _entries.ToArray();
    }

    public CapabilityAuditChainVerification VerifyChain()
    {
        var entries = _entries.ToArray();
        var anchor = Volatile.Read(ref _checkpoint.Current);
        var key = _auditHmacKey;
        if (key is null)
        {
            return new CapabilityAuditChainVerification
            {
                IsIntact = false,
                VerifiedEntryCount = 0,
                FirstBreakSequence = null,
                FirstBreakReason = $"{AUDIT_HMAC_KEY_ENV} is not configured",
                AnchorHash = anchor.Hash,
                AnchorSequence = anchor.Sequence
            };
        }

        var expectedPrevious = anchor.Hash;
        var expectedSequence = anchor.Sequence + 1;

        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];

            if (entry.Sequence != expectedSequence)
            {
                return new CapabilityAuditChainVerification
                {
                    IsIntact = false,
                    VerifiedEntryCount = index,
                    FirstBreakSequence = entry.Sequence,
                    FirstBreakReason = $"Sequence gap: expected {expectedSequence}, found {entry.Sequence}",
                    AnchorHash = anchor.Hash,
                    AnchorSequence = anchor.Sequence
                };
            }

            if (!string.Equals(entry.PreviousHash, expectedPrevious, StringComparison.Ordinal))
            {
                return new CapabilityAuditChainVerification
                {
                    IsIntact = false,
                    VerifiedEntryCount = index,
                    FirstBreakSequence = entry.Sequence,
                    FirstBreakReason = "PreviousHash does not match prior EntryHash",
                    AnchorHash = anchor.Hash,
                    AnchorSequence = anchor.Sequence
                };
            }

            var recomputed = ComputeEntryHash(
                key,
                entry.Sequence,
                entry.Timestamp,
                entry.Decision,
                entry.ConsumerId,
                entry.CapabilityName,
                entry.TicketId,
                entry.OutcomeCode ?? string.Empty,
                entry.Reason,
                entry.PrincipalUserId,
                entry.PrincipalTenantId,
                entry.PrincipalSessionId,
                entry.PrincipalIsAuthenticated,
                entry.PreviousHash);

            if (!string.Equals(recomputed, entry.EntryHash, StringComparison.Ordinal))
            {
                return new CapabilityAuditChainVerification
                {
                    IsIntact = false,
                    VerifiedEntryCount = index,
                    FirstBreakSequence = entry.Sequence,
                    FirstBreakReason = "EntryHash does not match recomputed canonical hash",
                    AnchorHash = anchor.Hash,
                    AnchorSequence = anchor.Sequence
                };
            }

            expectedPrevious = entry.EntryHash;
            expectedSequence = entry.Sequence + 1;
        }

        return new CapabilityAuditChainVerification
        {
            IsIntact = true,
            VerifiedEntryCount = entries.Length,
            FirstBreakSequence = null,
            FirstBreakReason = null,
            AnchorHash = anchor.Hash,
            AnchorSequence = anchor.Sequence
        };
    }

    private CapabilityAuditEntry Append(
        CapabilityAuditDecision decision,
        string consumerId,
        string capabilityName,
        string? ticketId,
        string outcomeCode,
        string? reason,
        AuditPrincipal principal)
    {
        EnsureDurableWriter();

        var key = _auditHmacKey
            ?? throw new InvalidOperationException($"{AUDIT_HMAC_KEY_ENV} is not configured; the capability audit chain refuses to record unkeyed entries");

        CapabilityAuditEntry entry;
        while (true)
        {
            var current = Volatile.Read(ref _chain.Current);
            var sequence = current.Sequence + 1;
            var timestamp = DateTime.UtcNow;
            var previousHash = current.LastHash;
            var entryHash = ComputeEntryHash(
                key,
                sequence,
                timestamp,
                decision,
                consumerId,
                capabilityName,
                ticketId,
                outcomeCode,
                reason,
                principal.UserId,
                principal.TenantId,
                principal.SessionId,
                principal.IsAuthenticated,
                previousHash);

            entry = new CapabilityAuditEntry
            {
                Sequence = sequence,
                Timestamp = timestamp,
                Decision = decision,
                ConsumerId = consumerId,
                CapabilityName = capabilityName,
                TicketId = ticketId,
                OutcomeCode = outcomeCode,
                Reason = reason,
                PrincipalUserId = principal.UserId,
                PrincipalTenantId = principal.TenantId,
                PrincipalSessionId = principal.SessionId,
                PrincipalIsAuthenticated = principal.IsAuthenticated,
                PreviousHash = previousHash,
                EntryHash = entryHash
            };

            if (Interlocked.CompareExchange(
                ref _chain.Current,
                new ChainState(sequence, entryHash),
                current) == current)
            {
                break;
            }
        }

        _durableSink.Writer.TryWrite(entry);
        _entries.Enqueue(entry);

        while (_entries.Count > MaxRetainedEntries
            && _entries.TryDequeue(out var evicted))
        {
            AdvanceCheckpoint(evicted);
        }

        Observe(LogLevel.Information, values: [("AuditChannel", AuditChannelName), ("Sequence", entry.Sequence), ("Decision", entry.Decision.ToString()), ("ConsumerId", entry.ConsumerId), ("CapabilityName", entry.CapabilityName), ("TicketId", entry.TicketId ?? string.Empty), ("OutcomeCode", entry.OutcomeCode ?? string.Empty), ("PrincipalUserId", entry.PrincipalUserId ?? string.Empty), ("PrincipalTenantId", entry.PrincipalTenantId ?? string.Empty), ("PrincipalSessionId", entry.PrincipalSessionId ?? string.Empty), ("PrincipalIsAuthenticated", entry.PrincipalIsAuthenticated), ("PreviousHash", entry.PreviousHash), ("EntryHash", entry.EntryHash)]);

        return entry;
    }

    private void AdvanceCheckpoint(CapabilityAuditEntry evicted)
    {
        while (true)
        {
            var current = Volatile.Read(ref _checkpoint.Current);
            if (evicted.Sequence <= current.Sequence)
            {
                return;
            }

            var next = new Checkpoint(evicted.Sequence, evicted.EntryHash);
            if (Interlocked.CompareExchange(
                ref _checkpoint.Current,
                next,
                current) == current)
            {
                return;
            }
        }
    }

    private async Task DrainDurableSinkAsync()
    {
        var reader = _durableSink.Reader;
        await using var writer = new StreamWriter(
            new FileStream(
                _durablePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read),
            Encoding.UTF8);

        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            CapabilityAuditEntry? last = null;
            while (reader.TryRead(out var entry))
            {
                last = entry;
                var line = JsonSerializer.Serialize(entry, DurableSerialization);
                await writer.WriteLineAsync(line).ConfigureAwait(false);
            }

            try
            {
                await writer.FlushAsync().ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                Observe(LogLevel.Error, ex, values: [("AuditChannel", AuditChannelName), ("DurablePath", _durablePath), ("Sequence", last?.Sequence ?? 0), ("DurableWriteFailed", true)]);
            }
            catch (UnauthorizedAccessException ex)
            {
                Observe(LogLevel.Error, ex, values: [("AuditChannel", AuditChannelName), ("DurablePath", _durablePath), ("Sequence", last?.Sequence ?? 0), ("DurableWriteFailed", true)]);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _durableSink.Writer.TryComplete();

        var writer = Volatile.Read(ref _durableWriter.Value);
        if (writer is not null)
        {
            await writer.ConfigureAwait(false);
        }
    }

    private static string ResolveDurablePath()
    {
        var configured = Environment.GetEnvironmentVariable(AUDIT_PATH_ENV);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var directory = Path.Combine(AppContext.BaseDirectory, "audit");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, DEFAULT_AUDIT_FILE);
    }

    private static byte[]? ResolveHmacKey()
    {
        var configured = Environment.GetEnvironmentVariable(AUDIT_HMAC_KEY_ENV);
        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        return Encoding.UTF8.GetBytes(configured);
    }

    private static string ComputeEntryHash(
        byte[] key,
        long sequence,
        DateTime timestamp,
        CapabilityAuditDecision decision,
        string consumerId,
        string capabilityName,
        string? ticketId,
        string outcomeCode,
        string? reason,
        string? userId,
        string? tenantId,
        string? sessionId,
        bool isAuthenticated,
        string previousHash)
    {
        var canonical = string.Concat(
            Frame(sequence.ToString(CultureInfo.InvariantCulture)),
            Frame(timestamp.ToString("O", CultureInfo.InvariantCulture)),
            Frame(decision.ToString()),
            Frame(consumerId),
            Frame(capabilityName),
            Frame(ticketId ?? string.Empty),
            Frame(outcomeCode),
            Frame(reason ?? string.Empty),
            Frame(userId ?? string.Empty),
            Frame(tenantId ?? string.Empty),
            Frame(sessionId ?? string.Empty),
            Frame(isAuthenticated ? "1" : "0"),
            Frame(previousHash));

        var digest = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexStringLower(digest);
    }

    private static string Frame(string field)
    {
        return $"{field.Length.ToString(CultureInfo.InvariantCulture)}:{field}";
    }
}
