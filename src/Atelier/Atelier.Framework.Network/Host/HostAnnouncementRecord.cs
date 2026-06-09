namespace Atelier.Framework.Network.Hosts;

public class HostAnnouncementRecord
{
    public string InstanceId { get; set; } = string.Empty;
    public string ServiceTypeName { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public Dictionary<string, string> Metadata { get; set; } = [];
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public HostState State { get; set; } = HostState.Starting;
}
