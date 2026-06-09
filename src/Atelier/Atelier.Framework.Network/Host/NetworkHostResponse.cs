using Atelier.Framework.Network.Hosts;

namespace Atelier.Framework.Network
{
    public class NetworkHostResponse
    {
        public bool IsSuccess { get; set; } = true;
        public string? ErrorMessage { get; set; }
        public string? ErrorCode { get; set; }

        public string InstanceId { get; set; } = string.Empty;
        public string HostTypeName { get; set; } = string.Empty;
        public string NetworkAddress { get; set; } = string.Empty;
        public DateTime LastHeartbeat { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = [];

        public static NetworkHostResponse FromAnnouncement(HostAnnouncementRecord announcement)
        {
            return new NetworkHostResponse
            {
                IsSuccess = true,
                InstanceId = announcement.InstanceId,
                HostTypeName = announcement.ServiceTypeName,
                NetworkAddress = announcement.Endpoint,
                LastHeartbeat = announcement.LastHeartbeat,
                Metadata = announcement.Metadata,
            };
        }

        public static NetworkHostResponse Failure(
            string errorMessage,
            string? errorCode = null)
        {
            return new NetworkHostResponse
            {
                IsSuccess = false,
                ErrorMessage = errorMessage,
                ErrorCode = errorCode
            };
        }
    }
}
