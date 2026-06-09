using Atelier.Framework.Observability;

namespace Atelier.Framework.Host.Execution.Hosts;

public partial class InProcessHost : IHost, IAtelier
{
    private HostState _state = HostState.Pending;

    public string HostId { get; } = Guid.NewGuid().ToString();
    public OfferingExecutionMode ExecutionMode => OfferingExecutionMode.InProcess;
    public HostState State => _state;

    public string? NetworkAddress => null;
    public int? NetworkPort => null;
    public int? ProcessId => Environment.ProcessId;

    public IReadOnlyDictionary<string, string> Metadata { get; } = new Dictionary<string, string>();

    public object? OfferingInstance { get; private set; }

    public void SetOfferingInstance(object offering)
    {
        OfferingInstance = offering ?? throw new ArgumentNullException(nameof(offering));
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _state = HostState.Starting;

        try
        {
            Observe(LogLevel.Debug);

            _state = HostState.Running;

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _state = HostState.Failed;

            Observe(LogLevel.Error, ex);

            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _state = HostState.Stopping;

        try
        {
            Observe(LogLevel.Debug);

            _state = HostState.Stopped;

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _state = HostState.Failed;

            Observe(LogLevel.Error, ex);

            throw;
        }
    }
}
