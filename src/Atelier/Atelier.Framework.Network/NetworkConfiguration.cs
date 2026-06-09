namespace Atelier.Framework.Network;

public class NetworkConnection
{
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public int Port { get; set; }
}
