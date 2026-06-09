public class {{ className }} : {{ ifaceName }}, IDisposable
{
    private readonly InProcessTransport _transport;
    private readonly global::Atelier.Framework.Network.Transport.ITransportPayloadCodec _codec;

    public {{ className }}(InProcessTransport transport,
                           global::Atelier.Framework.Network.Transport.ITransportPayloadCodec? codec = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _transport = transport;
        _codec = codec ?? global::Atelier.Framework.Network.Transport.JsonTransportPayloadCodec.Instance;
    }

    public bool IsConnected => _transport.IsConnected;

{{ methods }}

    public Task ConnectAsync(CancellationToken cancellationToken = default) => _transport.ConnectAsync(cancellationToken);

    public void Dispose()
    {
        _transport?.Dispose();
    }
}
