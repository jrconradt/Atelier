namespace Atelier.Framework.Host.Execution;

public interface IHost
{
    public string HostId { get; }
    public OfferingExecutionMode ExecutionMode { get; }
    public HostState State { get; }

    public Task StartAsync(CancellationToken cancellationToken = default);
    public Task StopAsync(CancellationToken cancellationToken = default);

    public string? NetworkAddress { get; }
    public int? NetworkPort { get; }
    public int? ProcessId { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }
}

public enum HostState
{
    Pending,
    Starting,
    Running,
    Stopping,
    Stopped,
    Failed
}
