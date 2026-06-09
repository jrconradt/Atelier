using Atelier.Framework.Attributes;
using Atelier.Framework.Network.Attributes;
using Atelier.Framework.Outcomes;

namespace Atelier.Framework.Network.Transport.Wiring;

[ContractAttribute("TransportEchoPayload", Version = "1.0", Namespace = "Framework.Network.Transport.Generated")]
public sealed class TransportEchoPayload
{
    public string Topic { get; set; } = string.Empty;
    public int Sequence { get; set; }
}

[InProcessTransport]
[HttpTransport]
public interface ITestEcho
{
    Task<Outcome> PingAsync(TransportEchoPayload request, CancellationToken cancellationToken);

    [RequiresAuthorization]
    Task<Outcome> SecurePingAsync(TransportEchoPayload request, CancellationToken cancellationToken);
}
