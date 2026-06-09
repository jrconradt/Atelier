using Atelier.Framework.Offering.Discovery;
using Atelier.Framework.Testing;

namespace Atelier.Framework.Offering;

public static class OfferingRegistryBehaviorTests
{
    private static OfferingAnnouncement AnnouncementFor(string instanceId,
                                                        string offeringTypeName)
    {
        return new OfferingAnnouncement
        {
            InstanceId = instanceId,
            OfferingTypeName = offeringTypeName,
        };
    }

    [GeneratedTest("Registry/Announce-Then-Lookup-Returns-Record", "global::Atelier.Framework.Offering.OfferingRegistry")]
    public static void AnnounceStoresRecordRetrievableByIdAndType()
    {
        var registry = new OfferingRegistry();

        var accepted = registry.Announce(AnnouncementFor("inst-1", "Echo"), "owner-a");
        if (!accepted)
        {
            throw new InvalidOperationException("Announce rejected a fresh instance");
        }

        var byId = registry.GetAnnouncementById("inst-1");
        if (byId is null
            || byId.InstanceId != "inst-1")
        {
            throw new InvalidOperationException($"GetAnnouncementById returned '{byId?.InstanceId}', expected 'inst-1'");
        }

        var byType = registry.GetAnnouncementsByOfferingType("Echo").ToList();
        if (byType.Count != 1
            || byType[0].InstanceId != "inst-1")
        {
            throw new InvalidOperationException($"GetAnnouncementsByOfferingType returned {byType.Count} records, expected 1 for inst-1");
        }

        var all = registry.GetAllAnnouncements().ToList();
        if (all.Count != 1)
        {
            throw new InvalidOperationException($"GetAllAnnouncements returned {all.Count}, expected 1");
        }
    }

    [GeneratedTest("Registry/Empty-Owner-Is-Rejected", "global::Atelier.Framework.Offering.OfferingRegistry")]
    public static void AnnounceWithEmptyOwnerIsRejected()
    {
        var registry = new OfferingRegistry();

        var accepted = registry.Announce(AnnouncementFor("inst-1", "Echo"), string.Empty);
        if (accepted)
        {
            throw new InvalidOperationException("Announce accepted an empty owner id");
        }

        if (registry.GetAnnouncementById("inst-1") is not null)
        {
            throw new InvalidOperationException("registry stored a record for an empty-owner announce");
        }
    }

    [GeneratedTest("Registry/Foreign-Owner-Cannot-Overwrite", "global::Atelier.Framework.Offering.OfferingRegistry")]
    public static void AnnounceFromDifferentOwnerIsRejected()
    {
        var registry = new OfferingRegistry();

        var first = registry.Announce(AnnouncementFor("inst-1", "Echo"), "owner-a");
        if (!first)
        {
            throw new InvalidOperationException("first Announce rejected");
        }

        var intruder = registry.Announce(AnnouncementFor("inst-1", "Echo"), "owner-b");
        if (intruder)
        {
            throw new InvalidOperationException("Announce accepted a re-announce from a different owner");
        }

        var sameOwner = registry.Announce(AnnouncementFor("inst-1", "Echo"), "owner-a");
        if (!sameOwner)
        {
            throw new InvalidOperationException("Announce rejected a re-announce from the original owner");
        }
    }

    [GeneratedTest("Registry/Foreign-Owner-Cannot-Revoke", "global::Atelier.Framework.Offering.OfferingRegistry")]
    public static void RevokeFromDifferentOwnerIsRejected()
    {
        var registry = new OfferingRegistry();
        registry.Announce(AnnouncementFor("inst-1", "Echo"), "owner-a");

        var intruder = registry.Revoke("inst-1", "owner-b");
        if (intruder)
        {
            throw new InvalidOperationException("Revoke accepted a different owner");
        }
        if (registry.GetAnnouncementById("inst-1") is null)
        {
            throw new InvalidOperationException("a foreign-owner Revoke removed the record");
        }

        var owner = registry.Revoke("inst-1", "owner-a");
        if (!owner)
        {
            throw new InvalidOperationException("Revoke rejected the original owner");
        }
        if (registry.GetAnnouncementById("inst-1") is not null)
        {
            throw new InvalidOperationException("record survived an owner Revoke");
        }
    }

    [GeneratedTest("Registry/Stale-Heartbeat-Marks-Announcement-Stale", "global::Atelier.Framework.Offering.OfferingRegistry")]
    public static void MissingHeartbeatSurfacesAsStaleAndPrunes()
    {
        var registry = new OfferingRegistry();
        registry.Announce(AnnouncementFor("fresh", "Echo"), "owner-a");

        var staleBefore = registry.GetStaleAnnouncements().ToList();
        if (staleBefore.Count != 0)
        {
            throw new InvalidOperationException($"a just-announced instance was reported stale ({staleBefore.Count})");
        }

        registry.UpdateHeartbeat("fresh");
        var stillFresh = registry.GetStaleAnnouncements().ToList();
        if (stillFresh.Count != 0)
        {
            throw new InvalidOperationException($"a heartbeat-refreshed instance was reported stale ({stillFresh.Count})");
        }

        registry.PruneStaleAnnouncements();
        if (registry.GetAnnouncementById("fresh") is null)
        {
            throw new InvalidOperationException("PruneStaleAnnouncements removed a fresh instance");
        }
    }
}
