using Atelier.Framework.Primitives;
using System.Net.Sockets;
using Atelier.Framework.Attributes;
using Atelier.Framework.Observability;
using Atelier.Framework.Requisitions;
using Atelier.Framework.Host.Execution.Hosts;

namespace Atelier.Framework.Host.Execution;

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class DockerExecutor : IExecutor, IAtelier
{
    [Requisite] protected readonly IDockerClientProvider _dockerClientProvider = null!;

    private const int READY_PROBE_TIMEOUT_SECONDS = 30;
    private const int READY_PROBE_INTERVAL_MS = 200;

    public OfferingExecutionMode ExecutionMode => OfferingExecutionMode.NetworkMapped;

    public async Task<HostExecutionContext> StartOfferingAsync(
        Type offeringType,
        ExecutionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(offeringType);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.DockerImage))
        {
            throw new ArgumentException(
                $"{nameof(ExecutionOptions.DockerImage)} must be set.",
                nameof(options));
        }

        var host = new DockerHost(_dockerClientProvider.Client, Logger);

        try
        {
            await host.CreateContainerAsync(options, cancellationToken).ConfigureAwait(false);

            await host.StartAsync(cancellationToken).ConfigureAwait(false);

            await WaitForEndpointReadyAsync(host.NetworkAddress, host.NetworkPort, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await CleanupFailedStartAsync(host).ConfigureAwait(false);
            throw;
        }

        Observe(LogLevel.Information, values: SecretRedaction.Redact(
            options.SecretClaims,
            ("OfferingType", offeringType.FullName ?? offeringType.Name),
            ("Image", options.DockerImage),
            ("NetworkAddress", host.NetworkAddress ?? "none"),
            ("NetworkPort", host.NetworkPort ?? 0)));

        return new HostExecutionContext
        {
            OfferingType = offeringType,
            OfferingTypeName = offeringType.FullName ?? offeringType.Name,
            ExecutionMode = OfferingExecutionMode.NetworkMapped,
            State = host.State,
            Host = host,
            NetworkAddress = host.NetworkAddress,
            NetworkPort = host.NetworkPort,
            ProcessId = host.ProcessId,
            StartedAt = DateTime.UtcNow,
            ResourceAllocation = options.ResourceLimits ?? new ResourceAllocation()
        };
    }

    public async Task StopOfferingAsync(
        HostExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Host is not null)
        {
            await context.Host.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        context.State = HostState.Stopped;
        context.StoppedAt = DateTime.UtcNow;
    }

    private async Task CleanupFailedStartAsync(DockerHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        try
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception cleanupEx)
        {
            Observe(LogLevel.Error, cleanupEx, values: [("Event", "Failed to clean up container after a failed start")]);
        }
    }

    private async Task WaitForEndpointReadyAsync(string? address, int? port, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(address) || !port.HasValue)
        {
            return;
        }

        var target = address == "0.0.0.0" ? "127.0.0.1" : address;
        var deadline = DateTime.UtcNow.AddSeconds(READY_PROBE_TIMEOUT_SECONDS);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await TryConnectAsync(target, port.Value, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(READY_PROBE_INTERVAL_MS, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"Endpoint {target}:{port.Value} did not accept connections within {READY_PROBE_TIMEOUT_SECONDS}s.");
    }

    private static async Task<bool> TryConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        using var socket = new Socket(
            AddressFamily.InterNetwork,
            SocketType.Stream,
            ProtocolType.Tcp);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(READY_PROBE_INTERVAL_MS * 5));

        try
        {
            await socket.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
            return socket.Connected;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
