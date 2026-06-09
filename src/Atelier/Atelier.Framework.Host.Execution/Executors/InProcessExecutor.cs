using Atelier.Framework.Primitives;
using Atelier.Framework.Attributes;
using Atelier.Framework.Observability;
using Atelier.Framework.Host.Execution.Hosts;

namespace Atelier.Framework.Host.Execution;

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class InProcessExecutor : IExecutor, IAtelier
{
    public OfferingExecutionMode ExecutionMode => OfferingExecutionMode.InProcess;

    public async Task<HostExecutionContext> StartOfferingAsync(
        Type offeringType,
        ExecutionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(offeringType);
        ArgumentNullException.ThrowIfNull(options);

        var host = new InProcessHost(Logger);
        await host.StartAsync(cancellationToken).ConfigureAwait(false);

        Observe(LogLevel.Information, values: [("OfferingType", offeringType.FullName ?? offeringType.Name), ("HostId", host.HostId)]);

        return new HostExecutionContext
        {
            OfferingType = offeringType,
            OfferingTypeName = offeringType.FullName ?? offeringType.Name,
            ExecutionMode = OfferingExecutionMode.InProcess,
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
}
