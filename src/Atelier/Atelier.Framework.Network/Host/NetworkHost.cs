namespace Atelier.Framework.Network;

public class NetworkHost
{
    public string Name { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Assembly { get; set; } = string.Empty;
    public IList<int> Ports { get; set; } = [];
    public IList<NetworkHost> Dependencies { get; set; } = [];
}
