using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Atelier.Framework.Context;
using Atelier.Framework.Network.Transport.Http;
using Atelier.Framework.Network.Transport.InProcess;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Resilience;
using Atelier.Framework.Testing;
using Microsoft.Extensions.Configuration;
using ILogger = Atelier.Framework.Observability.ILogger;

namespace Atelier.Framework.Network.Transport.Wiring;

public static class GeneratedTransportClientTests
{
    private static ResiliencePipelineFactory CreateResilience()
    {
        return new ResiliencePipelineFactory(new ConfigurationBuilder().Build(),
                                             AutoMockProvider.For<ILogger>());
    }

    private const string INPROC_TARGET = "global::Atelier.Framework.Network.Transport.Wiring.TestEchoInProcessTransport";
    private const string HTTP_TARGET = "global::Atelier.Framework.Network.Transport.Wiring.TestEchoHttpClient";
    private const string HTTP_SERVER_TARGET = "global::Atelier.Framework.Network.Transport.Wiring.TestEchoHttpServer";

    private sealed class RecordingEcho : ITestEcho
    {
        public readonly TaskCompletionSource<TransportEchoPayload> Received =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public readonly TaskCompletionSource<TransportEchoPayload> SecureReceived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Outcome> PingAsync(TransportEchoPayload request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            Received.TrySetResult(request);
            return Task.FromResult(Outcome.Success());
        }

        public Task<Outcome> SecurePingAsync(TransportEchoPayload request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            SecureReceived.TrySetResult(request);
            return Task.FromResult(Outcome.Success());
        }
    }

    private sealed class StubVerifier : IIdentityVerifier
    {
        public Task<Outcome<AuthorizationContext>> VerifyAsync(string token, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(token);
            var authorization = AuthorizationContext.Create(userId: "user-1", isVerified: true);
            return Task.FromResult(Outcome<AuthorizationContext>.Success(authorization));
        }
    }

    private static async Task<TransportEchoPayload> AwaitImplDeliveryAsync(
        TaskCompletionSource<TransportEchoPayload> source,
        string transport)
    {
        var winner = await Task.WhenAny(source.Task, Task.Delay(DELIVERY_TIMEOUT)).ConfigureAwait(false);
        if (winner != source.Task)
        {
            throw new InvalidOperationException($"{transport} server did not dispatch PingAsync to implementation within timeout");
        }
        return await source.Task.ConfigureAwait(false);
    }

    private static byte[] EncodePingAsyncPayload(string topic, int sequence)
    {
        return JsonSerializer.SerializeToUtf8Bytes(
            new TransportEchoPayload { Topic = topic, Sequence = sequence });
    }

    private static readonly TimeSpan DELIVERY_TIMEOUT = TimeSpan.FromSeconds(10);

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

    private static async Task<ITransportMessage> AwaitDeliveryAsync(
        TaskCompletionSource<ITransportMessage> source,
        string transport)
    {
        var winner = await Task.WhenAny(source.Task, Task.Delay(DELIVERY_TIMEOUT)).ConfigureAwait(false);
        if (winner != source.Task)
        {
            throw new InvalidOperationException($"{transport} did not deliver PingAsync within timeout");
        }
        return await source.Task.ConfigureAwait(false);
    }

    private static void AssertPingPayload(ITransportMessage message, string expectedTopic, int expectedSequence)
    {
        if (message.MessageType != "PingAsync")
        {
            throw new InvalidOperationException($"Expected MessageType 'PingAsync', got '{message.MessageType}'");
        }

        using var document = JsonDocument.Parse(message.Payload!);
        var topic = document.RootElement.GetProperty("Topic").GetString();
        var sequence = document.RootElement.GetProperty("Sequence").GetInt32();

        if (topic != expectedTopic)
        {
            throw new InvalidOperationException($"Payload Topic mismatch: '{topic}'");
        }
        if (sequence != expectedSequence)
        {
            throw new InvalidOperationException($"Payload Sequence mismatch: {sequence}");
        }
    }

    [GeneratedTest("transport.generated.inprocess.client-delivers-to-registered-handler", INPROC_TARGET)]
    public static async Task InProcessGeneratedClientForwardsMessageToHandler()
    {
        var transport = AutoMockProvider.For<InProcessTransport>();

        var delivered = new TaskCompletionSource<ITransportMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.RegisterMessageHandler((message, token) =>
        {
            delivered.TrySetResult(message);
            return Task.FromResult(Outcome.Success());
        });

        var client = new TestEchoInProcessTransport(transport);

        try
        {
            _ = client.PingAsync(
                new TransportEchoPayload { Topic = "generated-inproc", Sequence = 11 },
                CancellationToken.None);

            var message = await AwaitDeliveryAsync(delivered, "InProcess").ConfigureAwait(false);
            AssertPingPayload(message, "generated-inproc", 11);
        }
        finally
        {
            client.Dispose();
        }
    }

    [GeneratedTest("transport.generated.http.client-delivers-to-kestrel-handler", HTTP_TARGET)]
    public static async Task HttpGeneratedClientForwardsMessageToServerHandler()
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
        var client = new TestEchoHttpClient(httpClient,
                                            $"https://127.0.0.1:{port}",
                                            CreateResilience());

        try
        {
            _ = client.PingAsync(
                new TransportEchoPayload { Topic = "generated-http", Sequence = 22 },
                CancellationToken.None);

            var message = await AwaitDeliveryAsync(delivered, "HTTP").ConfigureAwait(false);
            AssertPingPayload(message, "generated-http", 22);
        }
        finally
        {
            client.Dispose();
            await server.StopAsync().ConfigureAwait(false);
        }
    }

    [GeneratedTest("transport.generated.http.requires-authorization-denies-unauthorized", HTTP_SERVER_TARGET)]
    public static async Task HttpGeneratedServerDeniesUnauthorizedSecureCall()
    {
        var port = FreeTcpPort();
        var impl = new RecordingEcho();
        var tlsOptions = TestTls.CreateSelfSignedOptions();
        var server = new TestEchoHttpServer(impl, port, tlsOptions);
        await server.StartAsync().ConfigureAwait(false);

        using var httpClient = TestTls.CreateLoopbackTrustingHttpClient();
        var runtimeClient = new HttpTransportClient(httpClient, $"https://127.0.0.1:{port}", CreateResilience());

        try
        {
            await runtimeClient.SendAsync(new TransportMessage
            {
                MessageType = "SecurePingAsync",
                Payload = EncodePingAsyncPayload("secure", 77)
            }).ConfigureAwait(false);

            var winner = await Task.WhenAny(impl.SecureReceived.Task, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
            if (winner == impl.SecureReceived.Task)
            {
                throw new InvalidOperationException("Unauthorized SecurePingAsync must not reach the implementation");
            }
        }
        finally
        {
            runtimeClient.Dispose();
            await server.StopAsync().ConfigureAwait(false);
            server.Dispose();
        }
    }

    [GeneratedTest("transport.generated.http.requires-authorization-admits-verified", HTTP_SERVER_TARGET)]
    public static async Task HttpGeneratedServerAdmitsVerifiedSecureCall()
    {
        var port = FreeTcpPort();
        var impl = new RecordingEcho();
        var tlsOptions = TestTls.CreateSelfSignedOptions();
        var server = new TestEchoHttpServer(impl, port, tlsOptions, new StubVerifier());
        await server.StartAsync().ConfigureAwait(false);

        using var httpClient = TestTls.CreateLoopbackTrustingHttpClient();
        var dto = TransportMessageDto.FromTransportMessage(new TransportMessage
        {
            MessageType = "SecurePingAsync",
            Payload = EncodePingAsyncPayload("secure-ok", 88)
        });
        var json = JsonSerializer.Serialize(dto, TransportJson.Options);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"https://127.0.0.1:{port}/transport/message")
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer test-token");
            await httpClient.SendAsync(request).ConfigureAwait(false);

            var received = await AwaitImplDeliveryAsync(impl.SecureReceived, "HTTP-secure").ConfigureAwait(false);
            if (received.Topic != "secure-ok"
                || received.Sequence != 88)
            {
                throw new InvalidOperationException($"Verified SecurePingAsync delivered wrong payload: Topic='{received.Topic}', Sequence={received.Sequence}");
            }
        }
        finally
        {
            await server.StopAsync().ConfigureAwait(false);
            server.Dispose();
        }
    }

    [GeneratedTest("transport.generated.http.server-dispatches-to-implementation", HTTP_SERVER_TARGET)]
    public static async Task HttpGeneratedServerDispatchesMessageToImplementation()
    {
        var port = FreeTcpPort();
        var impl = new RecordingEcho();
        var tlsOptions = TestTls.CreateSelfSignedOptions();
        var server = new TestEchoHttpServer(impl, port, tlsOptions);
        await server.StartAsync().ConfigureAwait(false);

        using var httpClient = TestTls.CreateLoopbackTrustingHttpClient();
        var runtimeClient = new HttpTransportClient(httpClient, $"https://127.0.0.1:{port}", CreateResilience());

        try
        {
            await runtimeClient.SendAsync(new TransportMessage
            {
                MessageType = "PingAsync",
                Payload = EncodePingAsyncPayload("server-http", 55)
            }).ConfigureAwait(false);

            var received = await AwaitImplDeliveryAsync(impl.Received, "HTTP").ConfigureAwait(false);
            if (received.Topic != "server-http" || received.Sequence != 55)
            {
                throw new InvalidOperationException($"HTTP server impl received wrong payload: Topic='{received.Topic}', Sequence={received.Sequence}");
            }
        }
        finally
        {
            runtimeClient.Dispose();
            await server.StopAsync().ConfigureAwait(false);
            server.Dispose();
        }
    }

}
