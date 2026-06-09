public class {{ className }} : IDisposable
{
    private readonly HttpTransportServer _server;
    private readonly {{ ifaceName }} _implementation;
    private readonly TransportTlsOptions? _tlsOptions;
    private readonly global::Atelier.Framework.Observability.ILogger? _logger;
    private readonly global::Atelier.Framework.Network.Transport.ITransportPayloadCodec _codec;

    public {{ className }}({{ ifaceName }} implementation,
                           int port = 80,
                           TransportTlsOptions? tlsOptions = null,
                           global::Atelier.Framework.Context.IIdentityVerifier? verifier = null,
                           global::Atelier.Framework.Observability.ILogger? logger = null,
                           global::Atelier.Framework.Network.Transport.ITransportPayloadCodec? codec = null)
    {
        ArgumentNullException.ThrowIfNull(implementation);
        _implementation = implementation;
        _tlsOptions = tlsOptions;
        _logger = logger;
        _codec = codec ?? global::Atelier.Framework.Network.Transport.JsonTransportPayloadCodec.Instance;
        _server = new HttpTransportServer(logger).Configure(HandleMessageAsync, port, tlsOptions, verifier);
    }

    public bool IsRunning => _server.IsRunning;

    private async Task<Outcome> HandleMessageAsync(ITransportMessage message, CancellationToken cancellationToken)
    {
        try
        {
            switch (message.MessageType)
            {
{{ cases }}
                default:
                    return Outcome.Failure();
            }
        }
        catch (Exception ex)
        {
            _logger?.WithError(ex)
                .WithMessage("Unhandled error while processing transport message")
                .WithLevel(global::Atelier.Framework.Observability.LogLevel.Error)
                .Log();
            return Outcome.Failure();
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _server.StartAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _server.StopAsync(cancellationToken);
    }

    public void Dispose()
    {
        _server.Dispose();
    }
}
