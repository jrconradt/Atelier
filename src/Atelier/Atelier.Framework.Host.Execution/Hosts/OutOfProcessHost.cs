using System.Diagnostics;
using Atelier.Framework.Observability;

namespace Atelier.Framework.Host.Execution.Hosts;

public partial class OutOfProcessHost : IHost, IAtelier, IAsyncDisposable
{
    private Process? _process;
    private HostState _state = HostState.Pending;
    private bool _disposed;

    public string HostId { get; } = Guid.NewGuid().ToString();
    public OfferingExecutionMode ExecutionMode => OfferingExecutionMode.OutOfProcess;
    public HostState State => _state;

    public string? NetworkAddress { get; private set; }
    public int? NetworkPort { get; private set; }
    public int? ProcessId => _process?.Id;

    public IReadOnlyDictionary<string, string> Metadata { get; } = new Dictionary<string, string>();

    public OutOfProcessHost Configure(
        Process process,
        string? networkAddress = null,
        int? networkPort = null)
    {
        _process = process ?? throw new ArgumentNullException(nameof(process));
        NetworkAddress = networkAddress;
        NetworkPort = networkPort;
        return this;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_process is null)
        {
            throw new InvalidOperationException(
                $"{nameof(OutOfProcessHost)} was not configured with a Process. Call Configure(process) before StartAsync.");
        }

        _state = HostState.Starting;

        try
        {
            Observe(LogLevel.Debug);

            if (!_process.Start())
            {
                throw new InvalidOperationException("Failed to start process");
            }

            _state = HostState.Running;

            Observe(LogLevel.Information, values: [("ProcessId", _process.Id), ("HostId", HostId)]);

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _state = HostState.Failed;

            Observe(LogLevel.Error, ex);

            DisposeFailedProcess();

            throw;
        }
    }

    private void DisposeFailedProcess()
    {
        if (_process is null)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception killEx)
        {
            Observe(LogLevel.Error, killEx, values: [("Event", "Failed to terminate process after a failed start")]);
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_process is null)
        {
            _state = HostState.Stopped;
            return;
        }

        _state = HostState.Stopping;

        try
        {
            Observe(LogLevel.Debug);

            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));

                try
                {
                    await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                }
            }

            _state = HostState.Stopped;
        }
        catch (Exception ex)
        {
            _state = HostState.Failed;

            Observe(LogLevel.Error, ex);

            throw;
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        await StopAsync().ConfigureAwait(false);
    }
}
