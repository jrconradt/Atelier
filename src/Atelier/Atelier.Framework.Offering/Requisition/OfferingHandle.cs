using Atelier.Framework.Primitives;
using Atelier.Framework.Attributes;
using Atelier.Framework.Network;
using Atelier.Framework.Infrastructure.Operation;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;
using Atelier.Framework.Host.Execution;

namespace Atelier.Framework.Offering.Requisition;

public interface IOfferingHandle : IAsyncDisposable
{
    public string InstanceId { get; }
    public string RequisitionId { get; }
    public Type OfferingType { get; }
    public OfferingExecutionMode ExecutionMode { get; }
    public Type Zone { get; }
    public bool IsAlive { get; }
    public Task<Outcome> HealthCheckAsync(CancellationToken cancellationToken = default);
    public Task<Outcome> ReleaseAsync(CancellationToken cancellationToken = default);
}

public interface IOfferingHandle<out T> : IOfferingHandle where T : class
{
    public T Offering { get; }
}

[ContractAttribute("OfferingHandle<T>", Version = "1.0", Namespace = "Framework.Offering.Requisition")]
public class OfferingHandle<T> : IOfferingHandle<T> where T : class
{
    [Requisite] protected readonly IOfferingManager _offeringManager = null!;
    [Requisite] protected readonly IOfferingProvider _offeringProvider = null!;
    private bool _disposed;

    public string InstanceId { get; }
    public string RequisitionId { get; }

    public OfferingHandle(
        OfferingRequisitionResult result,
        IOfferingManager offeringManager,
        IOfferingProvider offeringProvider)
    {
        InstanceId = result.InstanceId;
        RequisitionId = result.RequisitionId;
        ExecutionMode = result.ExecutionMode;
        Zone = result.PlacedZone;
        _offeringManager = offeringManager;
        _offeringProvider = offeringProvider;
    }

    public Type OfferingType => typeof(T);

    public OfferingExecutionMode ExecutionMode { get; }

    public Type Zone { get; }

    public bool IsAlive
    {
        get
        {
            var descriptor = _offeringManager.GetOfferingDescriptor(InstanceId);
            return descriptor is not null;
        }
    }

    public T Offering
    {
        get
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(OfferingHandle<T>));
            }

            var offering = _offeringProvider.GetOffering<T>();
            if (offering == null)
            {
                throw new InvalidOperationException(
                    $"Offering of type {typeof(T).FullName} with instance ID {InstanceId} is no longer available");
            }

            return offering;
        }
    }

    [Operation("HealthCheck")]
    public async Task<Outcome> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome.Failure();
        }

        var descriptor = _offeringManager.GetOfferingDescriptor(InstanceId);
        return descriptor is not null ? Outcome.Success() : Outcome.Failure();
    }

    [Operation("Release")]
    public async Task<Outcome> ReleaseAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome.Failure();
        }

        if (!_disposed)
        {
            var result = await _offeringManager.StopOfferingAsync(InstanceId, cancellationToken).ConfigureAwait(false);
            _disposed = true;
            return result;
        }

        return Outcome.Success();
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            await _offeringManager.StopOfferingAsync(InstanceId, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
