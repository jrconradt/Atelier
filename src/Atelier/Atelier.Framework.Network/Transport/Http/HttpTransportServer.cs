using Atelier.Framework.Primitives;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Atelier.Framework.Context;
using Atelier.Framework.Attributes;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Hosting;

namespace Atelier.Framework.Network.Transport.Http
{
    [Infrastructure(InfrastructureLifetime.Singleton)]
    [NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
    public partial class HttpTransportServer : IAtelier, ITransportServer, IAsyncDisposable
    {
        private const long MAX_REQUEST_BYTES = (TransportMessage.MAX_PAYLOAD_SIZE / 3L * 4L) + (256L * 1024L);
        private const int MAX_QUEUED_MESSAGES = 10_000;
        private const int DEFAULT_HTTPS_PORT = 8443;
        private const string TRANSPORT_HANDLER_ERROR = "TRANSPORT_HANDLER_ERROR";
        private const string TRANSPORT_QUEUE_FULL = "TRANSPORT_QUEUE_FULL";

        private readonly StrongBox<Func<ITransportMessage, CancellationToken, Task<Outcome>>?> _messageHandler = new(null);
        private readonly StrongBox<int> _port = new(DEFAULT_HTTPS_PORT);
        private readonly StrongBox<TransportTlsOptions> _tlsOptions = new(new TransportTlsOptions());
        private readonly StrongBox<IHost?> _host = new(null);
        private readonly ConcurrentQueue<TransportMessage> _messageQueue = new();
        private readonly StrongBox<global::Atelier.Framework.Context.IIdentityVerifier?> _verifier = new(null);
        private readonly StrongBox<int> _disposed = new(0);
        private readonly StrongBox<int> _lifecycleState = new(LIFECYCLE_STOPPED);

        private const int LIFECYCLE_STOPPED = 0;
        private const int LIFECYCLE_STARTING = 1;

        private bool IsDisposed => Volatile.Read(ref _disposed.Value) != 0;

        public HttpTransportServer Configure(
            Func<ITransportMessage, CancellationToken, Task<Outcome>> messageHandler,
            int port = DEFAULT_HTTPS_PORT,
            TransportTlsOptions? tlsOptions = null,
            global::Atelier.Framework.Context.IIdentityVerifier? verifier = null)
        {
            ArgumentNullException.ThrowIfNull(messageHandler);

            var options = tlsOptions ?? new TransportTlsOptions();
            options.Validate();

            _messageHandler.Value = messageHandler;
            _port.Value = port;
            _tlsOptions.Value = options;
            _verifier.Value = verifier;
            return this;
        }

        public bool IsRunning => _host.Value != null && !IsDisposed;

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (IsDisposed)
            {
                Observe(LogLevel.Warning, values: [("Operation", nameof(StartAsync)), ("Reason", $"{nameof(HttpTransportServer)} is disposed")]);
                return;
            }

            if (Interlocked.CompareExchange(ref _lifecycleState.Value, LIFECYCLE_STARTING, LIFECYCLE_STOPPED) != LIFECYCLE_STOPPED)
            {
                return;
            }

            _tlsOptions.Value!.Validate();

            var requireClientCert = _tlsOptions.Value.RequireClientCertificate || _tlsOptions.Value.RequiresMutualTls;

            _host.Value = new HostBuilder()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.ConfigureKestrel(options =>
                    {
                        options.Limits.MaxRequestBodySize = MAX_REQUEST_BYTES;
                        options.ListenAnyIP(_port.Value, listenOptions =>
                        {
                            listenOptions.UseHttps(httpsOptions =>
                            {
                                if (_tlsOptions.Value.HasCertificate)
                                {
                                    httpsOptions.ServerCertificate = _tlsOptions.Value.LoadCertificate();
                                }
                                httpsOptions.SslProtocols = _tlsOptions.Value.EnabledSslProtocols;
                                httpsOptions.CheckCertificateRevocation = _tlsOptions.Value.CheckCertificateRevocation;
                                if (requireClientCert)
                                {
                                    httpsOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                                }
                            });
                        });
                    });
                    webBuilder.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapPost("/transport/message", HandleMessageAsync);
                            endpoints.MapGet("/transport/messages", GetMessagesAsync);
                        });
                    });
                })
                .Build();

            await _host.Value.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        private readonly struct AuthResult
        {
            public Outcome Gate { get; }
            public string DenyReason { get; }
            public AuthorizationContext? Principal { get; }

            private AuthResult(Outcome gate,
                               string denyReason,
                               AuthorizationContext? principal)
            {
                Gate = gate;
                DenyReason = denyReason;
                Principal = principal;
            }

            public static AuthResult Permit(AuthorizationContext? principal)
                => new(Outcome.Success(), string.Empty, principal);

            public static AuthResult Deny(string reason)
                => new(Outcome.Failure(), reason, null);
        }

        private async Task<AuthResult> AuthenticateAsync(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            var options = _tlsOptions.Value!;

            if (options.RequiresClientCertificate)
            {
                var clientCertificate = context.Connection.ClientCertificate;
                if (clientCertificate == null)
                {
                    return AuthResult.Deny("Client certificate is required");
                }

                var validation = options.ClientCertificateValidation ?? new ClientCertificateValidation
                {
                    CheckRevocation = options.CheckCertificateRevocation
                };

                var certResult = validation.Validate(clientCertificate);
                if (!certResult.IsSuccess)
                {
                    return AuthResult.Deny("Client certificate validation failed");
                }

                return AuthResult.Permit(certResult.Data);
            }

            return await AuthenticateBearerAsync(context).ConfigureAwait(false);
        }

        private async Task<AuthResult> AuthenticateBearerAsync(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            var options = _tlsOptions.Value!;

            if (_verifier.Value == null)
            {
                if (options.AllowAnonymous)
                {
                    return AuthResult.Permit(null);
                }

                return AuthResult.Deny("No identity verifier is configured and anonymous access is not enabled");
            }

            if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader))
            {
                if (options.AllowAnonymous)
                {
                    return AuthResult.Permit(null);
                }

                return AuthResult.Deny("Bearer token is required");
            }

            var raw = authHeader.ToString();
            if (!raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return AuthResult.Deny("Authorization header must use the Bearer scheme");
            }

            var token = raw.Substring("Bearer ".Length).Trim();
            var verified = await _verifier.Value.VerifyAsync(token, context.RequestAborted).ConfigureAwait(false);
            if (!verified.IsSuccess
                || verified.Data == null)
            {
                return AuthResult.Deny("Bearer token verification failed");
            }

            return AuthResult.Permit(verified.Data);
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref _lifecycleState.Value, LIFECYCLE_STOPPED, LIFECYCLE_STARTING) != LIFECYCLE_STARTING)
            {
                return;
            }

            var host = _host.Value;
            _host.Value = null;

            if (host != null)
            {
                await host.StopAsync(cancellationToken).ConfigureAwait(false);
                host.Dispose();
            }
        }

        public void RegisterMessageHandler(Func<ITransportMessage, CancellationToken, Task<Outcome>> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);

        }

        private async Task HandleMessageAsync(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            var authentication = await AuthenticateAsync(context).ConfigureAwait(false);
            if (!authentication.Gate.IsSuccess)
            {
                Observe(LogLevel.Warning, values: [("Operation", nameof(HandleMessageAsync)), ("Reason", authentication.DenyReason)]);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { Error = "Authentication required" }).ConfigureAwait(false);
                return;
            }

            try
            {
                var dto = await JsonSerializer.DeserializeAsync<TransportMessageDto>(context.Request.Body, TransportJson.Options, context.RequestAborted).ConfigureAwait(false);

                if (dto == null)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsJsonAsync(new { Success = false, Error = "Empty request body" }).ConfigureAwait(false);
                    return;
                }

                var message = dto.ToTransportMessage();
                message.VerifiedAuthorization = authentication.Principal;

                if (_messageHandler.Value is not null)
                {
                    var outcome = await ProcessMessageAsync(message, context.RequestAborted).ConfigureAwait(false);
                    if (!outcome.IsSuccess)
                    {
                        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                        await context.Response.WriteAsJsonAsync(new
                        {
                            Success = false,
                            MessageId = message.MessageId,
                            ErrorCode = TRANSPORT_HANDLER_ERROR,
                            Error = "Message handling failed"
                        }).ConfigureAwait(false);
                        return;
                    }

                    var responseDto = TransportMessageDto.FromTransportMessage(message);
                    await context.Response.WriteAsJsonAsync(responseDto, TransportJson.Options).ConfigureAwait(false);
                    return;
                }

                if (_messageQueue.Count >= MAX_QUEUED_MESSAGES)
                {
                    Observe(LogLevel.Warning, values: [("Operation", nameof(HandleMessageAsync)), ("Reason", "Inbound message queue is full; applying backpressure")]);
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        Success = false,
                        ErrorCode = TRANSPORT_QUEUE_FULL,
                        Error = "Inbound message queue is full"
                    }).ConfigureAwait(false);
                    return;
                }

                _messageQueue.Enqueue(message);
                await context.Response.WriteAsJsonAsync(new { Success = true, MessageId = message.MessageId }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Observe(LogLevel.Warning, ex, values: [("Operation", nameof(HandleMessageAsync))]);
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { Error = "Invalid request" }).ConfigureAwait(false);
            }
        }

        private async Task GetMessagesAsync(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            var authentication = await AuthenticateAsync(context).ConfigureAwait(false);
            if (!authentication.Gate.IsSuccess)
            {
                Observe(LogLevel.Warning, values: [("Operation", nameof(GetMessagesAsync)), ("Reason", authentication.DenyReason)]);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { Error = "Authentication required" }).ConfigureAwait(false);
                return;
            }

            if (_messageQueue.TryDequeue(out var message))
            {
                var dto = TransportMessageDto.FromTransportMessage(message);
                await context.Response.WriteAsJsonAsync(dto).ConfigureAwait(false);
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
            }
        }

        private async Task<Outcome> ProcessMessageAsync(ITransportMessage message, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(message);
            try
            {
                if (_messageHandler.Value is null)
                {
                    Observe(LogLevel.Warning, values: [("MessageId", message.MessageId), ("Reason", "HttpTransportServer not configured with a message handler")]);
                    return Outcome.Failure();
                }

                return await _messageHandler.Value(message, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Observe(LogLevel.Error, ex, values: [("MessageId", message.MessageId)]);
                return Outcome.Failure();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed.Value, 1) != 0)
            {
                return;
            }

            await StopAsync().ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed.Value, 1) != 0)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _lifecycleState.Value, LIFECYCLE_STOPPED, LIFECYCLE_STARTING) != LIFECYCLE_STARTING)
            {
                return;
            }

            var host = _host.Value;
            _host.Value = null;

            if (host != null)
            {
                host.Dispose();
            }
        }
    }
}
