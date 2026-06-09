using System.Net;
using System.Net.Sockets;
using Atelier.Framework.Network.Transport.Http;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Resilience;
using Atelier.Framework.Testing;
using Microsoft.Extensions.Configuration;
using ILogger = Atelier.Framework.Observability.ILogger;

namespace Atelier.Framework.Network.Transport;

public static class TransportIntegrationTests
{
    private static ResiliencePipelineFactory CreateResilience()
    {
        return new ResiliencePipelineFactory(new ConfigurationBuilder().Build(),
                                             AutoMockProvider.For<ILogger>());
    }

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    [GeneratedTest("transport.http.roundtrips-over-kestrel", "global::Atelier.Framework.Network.Transport.Http.HttpTransportServer")]
    public static async Task HttpClientToServerRoundTrips()
    {
        var port = FreeTcpPort();
        var delivered = new TaskCompletionSource<ITransportMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tlsOptions = TestTls.CreateSelfSignedOptions();

        var server = AutoMockProvider.For<HttpTransportServer>();
        server.Configure(
            (message, token) =>
            {
                delivered.TrySetResult(message);
                return Task.FromResult(Outcome.Success());
            },
            port,
            tlsOptions);

        await server.StartAsync().ConfigureAwait(false);

        using var httpClient = TestTls.CreateLoopbackTrustingHttpClient();
        var client = new HttpTransportClient(httpClient, $"https://127.0.0.1:{port}", CreateResilience());

        try
        {
            await client.ConnectAsync().ConfigureAwait(false);
            var reply = await client.SendAsync(
                new TransportMessage { MessageId = "http-1", MessageType = "test" }).ConfigureAwait(false);

            if (reply is null)
            {
                throw new InvalidOperationException("HTTP transport returned no reply for the round-trip request");
            }

            if (reply.Headers.TryGetValue(TransportMessage.RESPONSE_ERROR_CODE_HEADER, out var errorCode))
            {
                reply.Headers.TryGetValue(TransportMessage.RESPONSE_ERROR_MESSAGE_HEADER, out var errorMessage);
                throw new InvalidOperationException($"HTTP transport round-trip failed with '{errorCode}': {errorMessage}");
            }

            if (reply.MessageId != "http-1")
            {
                throw new InvalidOperationException($"HTTP transport echoed '{reply.MessageId}', expected 'http-1'");
            }

            if (!delivered.Task.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("HTTP server acknowledged the request without invoking the registered message handler");
            }

            if (delivered.Task.Result.MessageId != "http-1")
            {
                throw new InvalidOperationException($"HTTP server handler received '{delivered.Task.Result.MessageId}', expected 'http-1'");
            }
        }
        finally
        {
            client.Dispose();
            await server.StopAsync().ConfigureAwait(false);
        }
    }
}
