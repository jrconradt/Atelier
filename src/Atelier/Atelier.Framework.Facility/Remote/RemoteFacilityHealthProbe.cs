using System.Net.Sockets;
using Atelier.Framework.Facility.Configuration;

namespace Atelier.Framework.Facility.Remote;

internal sealed class RemoteFacilityHealthProbe : IDisposable
{
    private readonly RemoteFacilityDescriptor _descriptor;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _connectTimeout;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _loop;

    public RemoteFacilityHealthProbe(
        RemoteFacilityDescriptor descriptor,
        RemoteFacilityConfiguration? config)
    {
        _descriptor = descriptor;
        var timeoutSeconds = config?.TimeoutSeconds ?? 30;
        _connectTimeout = TimeSpan.FromSeconds(timeoutSeconds);
        _interval = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds));
        _loop = RunAsync(_cancellation.Token);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_interval);

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            var healthy = await ProbeAsync(cancellationToken).ConfigureAwait(false);
            _descriptor.IsHealthy = healthy;
            _descriptor.LastHealthCheck = DateTime.UtcNow;
        }
    }

    private async Task<bool> ProbeAsync(CancellationToken cancellationToken)
    {
        if (!TryResolveTarget(_descriptor.Endpoint, out var host, out var port))
        {
            return false;
        }

        using var client = new TcpClient();
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attempt.CancelAfter(_connectTimeout);

        try
        {
            await client.ConnectAsync(host, port, attempt.Token).ConfigureAwait(false);
            return client.Connected;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static bool TryResolveTarget(
        string endpoint,
        out string host,
        out int port)
    {
        host = string.Empty;
        port = 0;

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return false;
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            return false;
        }

        host = uri.Host;
        port = uri.Port > 0
            ? uri.Port
            : (uri.Scheme == Uri.UriSchemeHttps ? 443 : 80);

        return !string.IsNullOrEmpty(host);
    }

    public void Dispose()
    {
        _cancellation.Cancel();

        _loop.ContinueWith(
            static (completed, state) =>
            {
                _ = completed.Exception;
                ((CancellationTokenSource)state!).Dispose();
            },
            _cancellation,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
