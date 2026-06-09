using Atelier.Framework.Attributes;
using Atelier.Framework.Outcomes;

namespace Atelier.Framework.Network.Hosts
{
    public interface IHostDiscovery
    {
        public Outcome Announce(HostAnnouncementRecord record);
        public Outcome UpdateHeartbeat(string instanceId);
        public Outcome Revoke(string instanceId);
        public Outcome<IEnumerable<HostAnnouncementRecord>> GetAllAnnouncements();
        public Outcome<IEnumerable<HostAnnouncementRecord>> GetAnnouncementsByServiceType(string serviceTypeName);
        public Outcome<HostAnnouncementRecord?> GetAnnouncementById(string instanceId);
        public Outcome<IEnumerable<HostAnnouncementRecord>> GetStaleAnnouncements();
        public Outcome PruneStaleAnnouncements();
    }


    public enum HostState
    {
        Created,
        Starting,
        Running,
        Stopping,
        Stopped,
        Failed,
        Committed,
        RolledBack
    }
}
