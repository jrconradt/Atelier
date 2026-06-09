using Atelier.Framework.Primitives;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Atelier.Framework.Attributes;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;

namespace Atelier.Framework.Network.Transport.InProcess
{
    [Infrastructure(InfrastructureLifetime.Singleton)]
    [NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
    public partial class InProcessTransport : IAtelier, ITransportClient, ITransportServer, IAsyncDisposable
    {
        private const int MAX_QUEUED_MESSAGES = 10_000;
        private const string TRANSPORT_HANDLER_ERROR = "HANDLER_ERROR";

        private readonly Channel<TransportMessage> _messageChannel = Channel.CreateBounded<TransportMessage>(
            new BoundedChannelOptions(MAX_QUEUED_MESSAGES)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false
            });
        private const int MAX_PUMP_RESTARTS = 5;
        private const int PUMP_RESTART_BACKOFF_MS = 100;
        private const int PUMP_RESTART_BACKOFF_MAX_MS = 5_000;

        private readonly StrongBox<Func<ITransportMessage, CancellationToken, Task<Outcome>>?> _messageHandler = new(null);
        private readonly StrongBox<CancellationTokenSource?> _cancellationTokenSource = new(null);
        private readonly StrongBox<Task?> _pumpTask = new(null);
        private readonly StrongBox<int> _disposed = new(0);
        private readonly StrongBox<int> _faulted = new(0);

        private bool IsDisposed => Volatile.Read(ref _disposed.Value) != 0;
        private bool IsFaulted => Volatile.Read(ref _faulted.Value) != 0;

        public bool IsConnected => !IsDisposed;
        public bool IsRunning => _cancellationTokenSource.Value != null && !IsDisposed;
        public bool IsHealthy => !IsDisposed && !IsFaulted;

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (IsDisposed)
            {
                Observe(LogLevel.Warning, values: [("Operation", nameof(ConnectAsync)), ("Reason", $"{nameof(InProcessTransport)} is disposed")]);
                return Task.CompletedTask;
            }

            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            var cancellation = Interlocked.Exchange(ref _cancellationTokenSource.Value, null);
            if (cancellation != null)
            {
                cancellation.Cancel();
                cancellation.Dispose();
            }

            return Task.CompletedTask;
        }

        public async Task<ITransportMessage?> SendAsync(ITransportMessage message, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(message);

            if (IsDisposed)
            {
                Observe(LogLevel.Warning, values: [("Operation", nameof(SendAsync)), ("MessageId", message.MessageId), ("Reason", $"{nameof(InProcessTransport)} is disposed")]);
                return null;
            }

            if (message is not TransportMessage transportMessage)
            {
                return null;
            }

            if (_messageHandler.Value != null)
            {
                return await InvokeHandlerAsync(transportMessage, cancellationToken).ConfigureAwait(false);
            }

            await _messageChannel.Writer.WriteAsync(transportMessage, cancellationToken).ConfigureAwait(false);
            return null;
        }

        private async Task<ITransportMessage> InvokeHandlerAsync(TransportMessage message, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(message);

            var reply = new TransportMessage
            {
                MessageId = message.MessageId,
                MessageType = message.MessageType
            };

            try
            {
                var outcome = await _messageHandler.Value!(message, cancellationToken).ConfigureAwait(false);
                if (outcome.IsSuccess)
                {
                    reply.Payload = message.Payload;
                    return reply;
                }

                reply.SetHeader(TransportMessage.RESPONSE_ERROR_CODE_HEADER, TRANSPORT_HANDLER_ERROR);
                return reply;
            }
            catch (Exception ex)
            {
                Observe(LogLevel.Error, ex, values: [("MessageId", message.MessageId)]);
                reply.SetHeader(TransportMessage.RESPONSE_ERROR_CODE_HEADER, TRANSPORT_HANDLER_ERROR);
                reply.SetHeader(TransportMessage.RESPONSE_ERROR_MESSAGE_HEADER, ex.Message);
                return reply;
            }
        }

        public async Task<ITransportMessage?> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            if (IsDisposed)
            {
                Observe(LogLevel.Warning, values: [("Operation", nameof(ReceiveAsync)), ("Reason", $"{nameof(InProcessTransport)} is disposed")]);
                return null;
            }

            try
            {
                return await _messageChannel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (IsDisposed)
            {
                Observe(LogLevel.Warning, values: [("Operation", nameof(StartAsync)), ("Reason", $"{nameof(InProcessTransport)} is disposed")]);
                return Task.CompletedTask;
            }

            var cancellation = new CancellationTokenSource();
            if (Interlocked.CompareExchange(ref _cancellationTokenSource.Value, cancellation, null) != null)
            {
                cancellation.Dispose();
                return Task.CompletedTask;
            }

            Volatile.Write(ref _faulted.Value, 0);
            _pumpTask.Value = ProcessMessagesAsync(cancellation.Token);

            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            await DisconnectAsync(cancellationToken).ConfigureAwait(false);

            var pump = Interlocked.Exchange(ref _pumpTask.Value, null);
            if (pump != null)
            {
                await pump.ConfigureAwait(false);
            }
        }

        public void RegisterMessageHandler(Func<ITransportMessage, CancellationToken, Task<Outcome>> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            _messageHandler.Value = handler;
        }

        private async Task ProcessMessagesAsync(CancellationToken cancellationToken)
        {
            var restarts = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await foreach (var message in _messageChannel.Reader.ReadAllAsync(cancellationToken))
                    {
                        await ProcessMessageAsync(message, cancellationToken).ConfigureAwait(false);
                    }

                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (restarts >= MAX_PUMP_RESTARTS)
                    {
                        Volatile.Write(ref _faulted.Value, 1);
                        Observe(LogLevel.Error, ex, values: [("Operation", nameof(ProcessMessagesAsync)), ("Restarts", restarts), ("Action", "PUMP_GIVE_UP")]);
                        return;
                    }

                    var backoff = ComputePumpBackoff(restarts);
                    restarts++;

                    Observe(LogLevel.Warning, ex, values: [("Operation", nameof(ProcessMessagesAsync)), ("Restart", restarts), ("BackoffMs", (long)backoff.TotalMilliseconds), ("Action", "PUMP_RESTART")]);

                    try
                    {
                        await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }

        private static TimeSpan ComputePumpBackoff(int restarts)
        {
            var scaled = PUMP_RESTART_BACKOFF_MS * Math.Pow(2, restarts);
            var capped = Math.Min(scaled, PUMP_RESTART_BACKOFF_MAX_MS);
            return TimeSpan.FromMilliseconds(capped);
        }

        private async Task ProcessMessageAsync(TransportMessage message, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(message);
            try
            {
                if (_messageHandler.Value != null)
                {
                    await _messageHandler.Value(message, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Observe(LogLevel.Error, ex, values: [("MessageId", message.MessageId)]);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed.Value, 1) != 0)
            {
                return;
            }

            _messageChannel.Writer.TryComplete();
            await DisconnectAsync().ConfigureAwait(false);

            var pump = Interlocked.Exchange(ref _pumpTask.Value, null);
            if (pump != null)
            {
                await pump.ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed.Value, 1) != 0)
            {
                return;
            }

            _messageChannel.Writer.TryComplete();

            var cancellation = Interlocked.Exchange(ref _cancellationTokenSource.Value, null);
            if (cancellation != null)
            {
                cancellation.Cancel();
                cancellation.Dispose();
            }
        }
    }
}
