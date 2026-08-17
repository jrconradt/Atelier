using System.Net.Http.Json;
using System.Text.Json;
using Atelier.Framework.Context;
using Atelier.Framework.Resilience;
using Polly;

namespace Atelier.Framework.Network.Transport.Http
{
    public class HttpTransportClient : ITransportClient
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly ResiliencePipeline _pipeline;
        private bool _disposed;

        public HttpTransportClient(HttpClient httpClient,
                                   string baseUrl,
                                   ResiliencePipelineFactory resilience)
        {
            ArgumentNullException.ThrowIfNull(httpClient);
            ArgumentNullException.ThrowIfNull(baseUrl);
            ArgumentNullException.ThrowIfNull(resilience);

            _httpClient = httpClient;
            if (_httpClient.Timeout == System.Threading.Timeout.InfiniteTimeSpan)
            {
                _httpClient.Timeout = DefaultTimeout;
            }
            _baseUrl = baseUrl.TrimEnd('/');
            _pipeline = resilience.HttpPipeline;
        }

        private CancellationTokenSource CreateCallCts(CancellationToken cancellationToken)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (!cancellationToken.CanBeCanceled)
            {
                cts.CancelAfter(DefaultTimeout);
            }

            return cts;
        }

        public bool IsConnected => !_disposed && _httpClient != null;

        public bool IsHealthy => IsConnected;

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public async Task<ITransportMessage?> SendAsync(ITransportMessage message, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(message);

            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(HttpTransportClient));
            }

            if (message is not TransportMessage transportMessage)
            {
                throw new ArgumentException("Message must be a TransportMessage", nameof(message));
            }

            ContextHeaderInjector.Stamp(transportMessage.Headers);

            var dto = TransportMessageDto.FromTransportMessage(transportMessage);

            using var cts = CreateCallCts(cancellationToken);
            return await _pipeline.ExecuteAsync(
                async token =>
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/transport/message")
                    {
                        Content = JsonContent.Create(dto, options: TransportJson.Options)
                    };
                    ContextHeaderInjector.Apply(request);

                    var response = await _httpClient.SendAsync(request, token).ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        if (response.Content.Headers.ContentLength == 0)
                        {
                            return (ITransportMessage?)new TransportMessage
                            {
                                MessageId = transportMessage.MessageId,
                                MessageType = transportMessage.MessageType
                            };
                        }

                        var responseDto = await response.Content.ReadFromJsonAsync<TransportMessageDto>(TransportJson.Options, token).ConfigureAwait(false);
                        return responseDto?.ToTransportMessage();
                    }

                    var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                    var failure = ParseFailure(body);
                    var reply = new TransportMessage
                    {
                        MessageId = transportMessage.MessageId,
                        MessageType = transportMessage.MessageType
                    };
                    reply.SetHeader(TransportMessage.RESPONSE_ERROR_CODE_HEADER, failure.Code);
                    reply.SetHeader(TransportMessage.RESPONSE_ERROR_MESSAGE_HEADER, failure.Message);
                    return reply;
                },
                cts.Token).ConfigureAwait(false);
        }

        private static (string Code, string Message) ParseFailure(string body)
        {
            if (string.IsNullOrEmpty(body))
            {
                return ("TRANSPORT_ERROR", "Transport request failed");
            }

            try
            {
                using var document = JsonDocument.Parse(body);
                var code = document.RootElement.TryGetProperty("ErrorCode", out var codeElement)
                    ? codeElement.GetString() ?? "TRANSPORT_ERROR"
                    : "TRANSPORT_ERROR";
                var error = document.RootElement.TryGetProperty("Error", out var errorElement)
                    ? errorElement.GetString() ?? "Transport request failed"
                    : "Transport request failed";
                return (code, error);
            }
            catch (JsonException)
            {
                return ("TRANSPORT_ERROR", body);
            }
        }

        public async Task<ITransportMessage?> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(HttpTransportClient));
            }

            using var cts = CreateCallCts(cancellationToken);
            return await _pipeline.ExecuteAsync(
                async token =>
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/transport/messages");
                    ContextHeaderInjector.Apply(request);

                    var response = await _httpClient.SendAsync(request, token).ConfigureAwait(false);

                    if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                    {
                        return (ITransportMessage?)null;
                    }

                    if (response.IsSuccessStatusCode)
                    {
                        if (response.Content.Headers.ContentLength == 0)
                        {
                            return (ITransportMessage?)null;
                        }

                        var dto = await response.Content.ReadFromJsonAsync<TransportMessageDto>(TransportJson.Options, token).ConfigureAwait(false);
                        return dto?.ToTransportMessage();
                    }

                    throw new HttpRequestException($"HTTP transport receive failed: {response.StatusCode}");
                },
                cts.Token).ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _httpClient?.Dispose();
                _disposed = true;
            }
        }
    }
}
