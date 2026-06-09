public class {{ className }} : {{ ifaceName }}, IDisposable
{
    private readonly HttpTransportClient _transport;
    private readonly global::Atelier.Framework.Network.Transport.ITransportPayloadCodec _codec;

    public {{ className }}(HttpClient httpClient,
                           string endpoint,
                           global::Atelier.Framework.Resilience.ResiliencePipelineFactory resilience,
                           global::Atelier.Framework.Network.Transport.ITransportPayloadCodec? codec = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(resilience);
        _transport = new HttpTransportClient(httpClient,
                                             endpoint,
                                             resilience);
        _codec = codec ?? global::Atelier.Framework.Network.Transport.JsonTransportPayloadCodec.Instance;
    }

    public bool IsHealthy => _transport.IsHealthy;

{{ methods }}

    public void Dispose()
    {
        _transport?.Dispose();
    }
}
