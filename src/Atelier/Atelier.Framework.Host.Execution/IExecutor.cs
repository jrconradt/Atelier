namespace Atelier.Framework.Host.Execution;

public interface IExecutor
{
    public OfferingExecutionMode ExecutionMode { get; }

    public Task<HostExecutionContext> StartOfferingAsync(
        Type offeringType,
        ExecutionOptions options,
        CancellationToken cancellationToken = default);

    public Task StopOfferingAsync(
        HostExecutionContext context,
        CancellationToken cancellationToken = default);
}
