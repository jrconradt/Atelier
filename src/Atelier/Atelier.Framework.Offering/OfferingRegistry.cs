using Atelier.Framework.Primitives;
using System.Collections.Concurrent;
using Atelier.Framework.Attributes;
using Atelier.Framework.Offering.Discovery;

namespace Atelier.Framework.Offering;

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class OfferingRegistry : IOfferingRegistry
{
    private const int MaxAnnouncements = 50_000;
    private readonly ConcurrentDictionary<string, OfferingAnnouncement> _announcements = new();
    private readonly ConcurrentDictionary<string, string> _announcementOwners = new();
    private readonly ConcurrentDictionary<string, long> _heartbeats = new();
    private readonly TimeSpan _heartbeatTimeout = TimeSpan.FromSeconds(30);
    public bool Announce(OfferingAnnouncement record, string ownerId)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (string.IsNullOrEmpty(ownerId))
        {
            return false;
        }

        var establishedOwner = _announcementOwners.GetOrAdd(record.InstanceId, ownerId);
        if (!string.Equals(establishedOwner, ownerId, StringComparison.Ordinal))
        {
            return false;
        }

        if (!_announcements.ContainsKey(record.InstanceId)
            && _announcements.Count >= MaxAnnouncements)
        {
            PruneStaleAnnouncements();
            if (_announcements.Count >= MaxAnnouncements)
            {
                _announcementOwners.TryRemove(new KeyValuePair<string, string>(record.InstanceId, ownerId));
                return false;
            }
        }

        _heartbeats[record.InstanceId] = DateTime.UtcNow.Ticks;
        _announcements[record.InstanceId] = record;
        return true;
    }

    public void UpdateHeartbeat(string instanceId)
    {
        ArgumentNullException.ThrowIfNull(instanceId);

        if (_announcements.ContainsKey(instanceId))
        {
            _heartbeats[instanceId] = DateTime.UtcNow.Ticks;
        }
    }

    public bool Revoke(string instanceId, string ownerId)
    {
        ArgumentNullException.ThrowIfNull(instanceId);

        if (string.IsNullOrEmpty(ownerId))
        {
            return false;
        }

        if (_announcementOwners.TryGetValue(
            instanceId,
            out var existingOwner)
            && !string.Equals(existingOwner, ownerId, StringComparison.Ordinal))
        {
            return false;
        }

        _announcementOwners.TryRemove(
            instanceId,
            out _);
        _announcements.TryRemove(
            instanceId,
            out _);
        _heartbeats.TryRemove(
            instanceId,
            out _);
        return true;
    }

    public IEnumerable<OfferingAnnouncement> GetAllAnnouncements()
    {
        return _announcements.Values.ToList();
    }

    public IEnumerable<OfferingAnnouncement> GetAnnouncementsByOfferingType(string offeringTypeName)
    {
        ArgumentNullException.ThrowIfNull(offeringTypeName);

        return _announcements.Values
            .Where(a => a.OfferingTypeName == offeringTypeName)
            .ToList();
    }

    public OfferingAnnouncement? GetAnnouncementById(string instanceId)
    {
        ArgumentNullException.ThrowIfNull(instanceId);

        _announcements.TryGetValue(
            instanceId,
            out var record);
        return record;
    }

    public IEnumerable<OfferingAnnouncement> GetStaleAnnouncements()
    {
        var cutoffTicks = (DateTime.UtcNow - _heartbeatTimeout).Ticks;

        return _announcements.Values
            .Where(a => !_heartbeats.TryGetValue(a.InstanceId, out var lastTicks)
                || lastTicks < cutoffTicks)
            .ToList();
    }

    public void PruneStaleAnnouncements()
    {
        var staleAnnouncements = GetStaleAnnouncements();

        foreach (var announcement in staleAnnouncements)
        {
            _announcements.TryRemove(
                announcement.InstanceId,
                out _);
            _announcementOwners.TryRemove(
                announcement.InstanceId,
                out _);
            _heartbeats.TryRemove(
                announcement.InstanceId,
                out _);
        }
    }
}
