using Atelier.Framework.Primitives;
using System.Collections.Concurrent;
using Atelier.Framework.Attributes;
using Atelier.Framework.Network.Hosts;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;
using Atelier.Framework.Infrastructure;

namespace Atelier.Framework.Network
{
    [Infrastructure(InfrastructureLifetime.Singleton)]
    [NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
    public partial class NetworkHostDiscovery : IAtelier, IHostDiscovery
    {
        private readonly ConcurrentDictionary<string, HostAnnouncementRecord> _announcements = new();
        private readonly Timer? _cleanupTimer;
        private readonly TimeSpan _heartbeatTimeout = TimeSpan.FromMinutes(5);
        private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(1);

        public NetworkHostDiscovery()
        {
            _cleanupTimer = new Timer(CleanupStaleAnnouncements, null, _cleanupInterval, _cleanupInterval);
        }

        public Outcome Announce(HostAnnouncementRecord record)
        {
            if (record is null)
            {
                Observe(LogLevel.Warning, values: [("Operation", nameof(Announce)), ("Reason", "Announcement record was null")]);
                return Outcome.Failure();
            }

            if (string.IsNullOrWhiteSpace(record.InstanceId))
            {
                Observe(LogLevel.Warning, values: [("Operation", nameof(Announce)), ("Reason", "Instance id was null or whitespace")]);
                return Outcome.Failure();
            }

            using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "Host", record.InstanceId);

            if (string.IsNullOrWhiteSpace(record.ServiceName))
            {
                Observe(LogLevel.Warning, values: [("Operation", nameof(Announce)), ("Reason", "Service name was null or whitespace"), ("InstanceId", record.InstanceId)]);
                return Outcome.Failure();
            }

            if (string.IsNullOrWhiteSpace(record.Endpoint))
            {
                Observe(LogLevel.Warning, values: [("Operation", nameof(Announce)), ("Reason", "Endpoint was null or whitespace"), ("InstanceId", record.InstanceId)]);
                return Outcome.Failure();
            }

            record.LastHeartbeat = DateTime.UtcNow;
            record.CreatedAt = record.CreatedAt == default ? DateTime.UtcNow : record.CreatedAt;

            _announcements[record.InstanceId] = record;

            Observe(LogLevel.Information, values: [("ServiceName", record.ServiceName), ("InstanceId", record.InstanceId), ("Endpoint", record.Endpoint)]);

            return Outcome.Success();
        }

        public Outcome UpdateHeartbeat(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                Observe(LogLevel.Warning, values: [("Operation", nameof(UpdateHeartbeat)), ("Reason", "Instance id was null or whitespace")]);
                return Outcome.Failure();
            }

            using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "Host", instanceId);

            if (_announcements.TryGetValue(instanceId, out var record))
            {
                record.LastHeartbeat = DateTime.UtcNow;
                _announcements[instanceId] = record;
            }

            return Outcome.Success();
        }

        public Outcome Revoke(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                Observe(LogLevel.Warning, values: [("Operation", nameof(Revoke)), ("Reason", "Instance id was null or whitespace")]);
                return Outcome.Failure();
            }

            using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "Host", instanceId);

            if (_announcements.TryRemove(instanceId, out var record))
            {
                Observe(LogLevel.Information, values: [("ServiceName", record.ServiceName), ("InstanceId", instanceId)]);
                return Outcome.Success();
            }

            Observe(LogLevel.Information, values: [("Operation", nameof(Revoke)), ("Reason", "Revoke of absent announcement treated as success"), ("InstanceId", instanceId)]);
            return Outcome.Success();
        }

        public Outcome<IEnumerable<HostAnnouncementRecord>> GetAllAnnouncements()
        {
            return Outcome<IEnumerable<HostAnnouncementRecord>>.Success(_announcements.Values.ToList());
        }

        public Outcome<IEnumerable<HostAnnouncementRecord>> GetAnnouncementsByServiceType(string serviceTypeName)
        {
            if (string.IsNullOrWhiteSpace(serviceTypeName))
            {
                Observe(LogLevel.Warning, values: [("Operation", nameof(GetAnnouncementsByServiceType)), ("Reason", "Service type name was null or whitespace")]);
                return Outcome<IEnumerable<HostAnnouncementRecord>>.Failure();
            }

            var matches = _announcements.Values
                .Where(a => a.ServiceTypeName.Equals(serviceTypeName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return Outcome<IEnumerable<HostAnnouncementRecord>>.Success(matches);
        }

        public Outcome<HostAnnouncementRecord?> GetAnnouncementById(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                Observe(LogLevel.Warning, values: [("Operation", nameof(GetAnnouncementById)), ("Reason", "Instance id was null or whitespace")]);
                return Outcome<HostAnnouncementRecord?>.Failure();
            }

            using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "Host", instanceId);

            _announcements.TryGetValue(instanceId, out var record);
            return Outcome<HostAnnouncementRecord?>.Success(record);
        }

        public Outcome<IEnumerable<HostAnnouncementRecord>> GetStaleAnnouncements()
        {
            var cutoff = DateTime.UtcNow - _heartbeatTimeout;
            var stale = _announcements.Values
                .Where(a => a.LastHeartbeat < cutoff)
                .ToList();

            return Outcome<IEnumerable<HostAnnouncementRecord>>.Success(stale);
        }

        public Outcome PruneStaleAnnouncements()
        {
            var staleOutcome = GetStaleAnnouncements();
            var staleAnnouncements = staleOutcome.Data?.ToList() ?? new List<HostAnnouncementRecord>();
            foreach (var announcement in staleAnnouncements)
            {
                Revoke(announcement.InstanceId);
            }

            if (staleAnnouncements.Count > 0)
            {
                Observe(LogLevel.Information, values: [("Count", staleAnnouncements.Count)]);
            }

            return Outcome.Success();
        }

        private void CleanupStaleAnnouncements(object? state)
        {
            try
            {
                PruneStaleAnnouncements();
            }
            catch (Exception ex)
            {
                Observe(LogLevel.Error, ex);
            }
        }
    }
}
