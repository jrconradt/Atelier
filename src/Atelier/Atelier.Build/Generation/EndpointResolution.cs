using Atelier.Build.Discovery;

namespace Atelier.Build.Generation;

public static class EndpointResolution
{
    public static int? ResolveGravityPort(BoutiqueYamlSchema schema)
    {
        return schema.Kestrel?.Endpoints?
            .FirstOrDefault(e => e.Name == "gravity" || e.Name == "cluster")?.Port;
    }

    public static string UdpSuffixFor(int port, int? gravityPort)
    {
        return port == gravityPort ? "/udp" : string.Empty;
    }
}
