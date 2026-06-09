using Atelier.Framework.Offering.Discovery;

namespace Atelier.Framework.Offering
{
    public interface IOfferingRegistry
    {
        public bool Announce(OfferingAnnouncement offering, string ownerId);
        public void UpdateHeartbeat(string instanceId);
        public bool Revoke(string instanceId, string ownerId);
        public IEnumerable<OfferingAnnouncement> GetAllAnnouncements();
        public IEnumerable<OfferingAnnouncement> GetAnnouncementsByOfferingType(string offeringTypeName);
        public OfferingAnnouncement? GetAnnouncementById(string instanceId);
        public IEnumerable<OfferingAnnouncement> GetStaleAnnouncements();
        public void PruneStaleAnnouncements();
    }
}
