using Atelier.Framework.Outcomes;
using Atelier.Framework.Testing;

namespace Atelier.Framework.Network.Transport.InProcess;

public static class InProcessTransportTests
{
    private const string TARGET = "global::Atelier.Framework.Network.Transport.InProcess.InProcessTransport";

    [GeneratedTest("transport.inprocess.send-receive-roundtrips", TARGET)]
    public static async Task SendThenReceiveRoundTripsTheMessage()
    {
        var transport = AutoMockProvider.For<InProcessTransport>();

        var sent = new TransportMessage
        {
            MessageId = "roundtrip-1",
            MessageType = "test",
            Payload = System.Text.Encoding.UTF8.GetBytes("hello")
        };

        await transport.SendAsync(sent).ConfigureAwait(false);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var received = await transport.ReceiveAsync(timeout.Token).ConfigureAwait(false);

        if (received is null)
        {
            throw new InvalidOperationException("ReceiveAsync returned null");
        }

        if (received.MessageId != "roundtrip-1")
        {
            throw new InvalidOperationException($"Expected MessageId 'roundtrip-1', got '{received.MessageId}'");
        }
    }

    [GeneratedTest("transport.inprocess.registered-handler-invoked", TARGET)]
    public static async Task RegisteredHandlerReceivesSentMessage()
    {
        var transport = AutoMockProvider.For<InProcessTransport>();

        var handled = new TaskCompletionSource<ITransportMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        transport.RegisterMessageHandler((message, token) =>
        {
            handled.TrySetResult(message);
            return Task.FromResult(Outcome.Success());
        });

        await transport.StartAsync().ConfigureAwait(false);

        await transport.SendAsync(
            new TransportMessage { MessageId = "handler-1", MessageType = "test" }).ConfigureAwait(false);

        var winner = await Task.WhenAny(handled.Task, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
        if (winner != handled.Task)
        {
            throw new InvalidOperationException("Registered handler was not invoked within timeout");
        }

        var delivered = await handled.Task.ConfigureAwait(false);
        if (delivered.MessageId != "handler-1")
        {
            throw new InvalidOperationException($"Handler received wrong message '{delivered.MessageId}'");
        }
    }

    [GeneratedTest("transport.inprocess.handler-invoked-once-per-message", TARGET)]
    public static async Task StartedHandlerIsInvokedExactlyOncePerSend()
    {
        var transport = AutoMockProvider.For<InProcessTransport>();

        var invocations = 0;
        transport.RegisterMessageHandler((message, token) =>
        {
            Interlocked.Increment(ref invocations);
            return Task.FromResult(Outcome.Success());
        });

        await transport.StartAsync().ConfigureAwait(false);

        await transport.SendAsync(
            new TransportMessage { MessageId = "once-1", MessageType = "test" }).ConfigureAwait(false);

        using var settle = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (Volatile.Read(ref invocations) == 0
               && !settle.Token.IsCancellationRequested)
        {
            await Task.Delay(10, settle.Token).ConfigureAwait(false);
        }

        await Task.Delay(100).ConfigureAwait(false);

        var count = Volatile.Read(ref invocations);
        if (count != 1)
        {
            throw new InvalidOperationException($"Expected handler to be invoked exactly once, got {count}");
        }
    }

    [GeneratedTest("transport.inprocess.running-toggles-with-lifecycle", TARGET)]
    public static async Task RunningTogglesWithStartAndStop()
    {
        var transport = AutoMockProvider.For<InProcessTransport>();

        if (transport.IsRunning)
        {
            throw new InvalidOperationException("Transport should not be running before StartAsync");
        }

        await transport.StartAsync().ConfigureAwait(false);
        if (!transport.IsRunning)
        {
            throw new InvalidOperationException("Transport should be running after StartAsync");
        }

        await transport.StopAsync().ConfigureAwait(false);
        if (transport.IsRunning)
        {
            throw new InvalidOperationException("Transport should not be running after StopAsync");
        }
    }

    [GeneratedTest("transport.inprocess.disconnects-on-dispose", TARGET)]
    public static void DisposeMarksDisconnected()
    {
        var transport = AutoMockProvider.For<InProcessTransport>();

        if (!transport.IsConnected)
        {
            throw new InvalidOperationException("Transport should be connected before Dispose");
        }

        transport.Dispose();

        if (transport.IsConnected)
        {
            throw new InvalidOperationException("Transport should be disconnected after Dispose");
        }
    }
}
