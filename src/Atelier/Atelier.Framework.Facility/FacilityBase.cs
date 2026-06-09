using System.Collections.Concurrent;
using Atelier.Framework.Infrastructure;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Host.Execution;

namespace Atelier.Framework.Facility;

public abstract partial class FacilityBase : IAtelier, IFacility
{
    private readonly ConcurrentDictionary<string, AllocatedResource> _allocatedResources = new();
    private ResourceAllocation? _availableSlot;

    protected FacilityCapabilities Capabilities { get; } = new();

    private ResourceAllocation ReadAvailable()
    {
        var slot = Volatile.Read(ref _availableSlot);
        if (slot is not null)
        {
            return slot;
        }

        var seeded = Capabilities.CurrentAvailable;
        var prior = Interlocked.CompareExchange(ref _availableSlot, seeded, null);
        return prior ?? seeded;
    }

    private bool TrySwapAvailable(
        ResourceAllocation expected,
        ResourceAllocation next)
    {
        if (!ReferenceEquals(Interlocked.CompareExchange(ref _availableSlot, next, expected), expected))
        {
            return false;
        }

        Capabilities.CurrentAvailable = next;
        return true;
    }

    public abstract string FacilityId { get; }
    public abstract string FacilityName { get; }
    public abstract FacilityType Type { get; }

    FacilityCapabilities IFacility.Capabilities => GetCurrentCapabilities();

    protected FacilityBase()
    {
        InitializeCapabilities();
    }

    protected abstract void InitializeCapabilities();

    public abstract bool CanFulfill(IRequirement requirement);

    public abstract Task<Outcome<ResourceAvailability>> CheckResourceAvailabilityAsync(
        ResourceAllocation requested,
        CancellationToken cancellationToken);

    public abstract Task<Outcome<ProvisionTicket>> ProvisionAsync(
        IRequirement requirement,
        CancellationToken cancellationToken);

    public abstract Task<Outcome> ReleaseAsync(
        string ticketId,
        CancellationToken cancellationToken);

    private FacilityCapabilities GetCurrentCapabilities()
    {
        return new FacilityCapabilities
        {
            SupportedScopes = Capabilities.SupportedScopes,
            CanProvide = Capabilities.CanProvide,
            TotalCapacity = Capabilities.TotalCapacity,
            CurrentAvailable = ReadAvailable(),
            Zone = Capabilities.Zone,
            Metadata = Capabilities.Metadata
        };
    }

    protected bool TryAllocateResources(
        string furnishingId,
        ResourceAllocation requested,
        out ResourceAllocation allocated)
    {
        allocated = new ResourceAllocation();

        while (true)
        {
            var current = ReadAvailable();
            var nextAllocated = new ResourceAllocation();
            var nextAvailable = new ResourceAllocation
            {
                MaxMemoryBytes = current.MaxMemoryBytes,
                MaxCpuPercent = current.MaxCpuPercent,
                MaxThreads = current.MaxThreads,
                MaxConnections = current.MaxConnections
            };

            if (requested.MaxMemoryBytes.HasValue)
            {
                if (!current.MaxMemoryBytes.HasValue
                    || current.MaxMemoryBytes.Value < requested.MaxMemoryBytes.Value)
                {
                    Observe(LogLevel.Warning, values: [("Requested", requested.MaxMemoryBytes.Value), ("Available", current.MaxMemoryBytes ?? 0)]);
                    return false;
                }
                nextAllocated.MaxMemoryBytes = requested.MaxMemoryBytes.Value;
                nextAvailable.MaxMemoryBytes -= requested.MaxMemoryBytes.Value;
            }

            if (requested.MaxCpuPercent.HasValue)
            {
                if (!current.MaxCpuPercent.HasValue
                    || current.MaxCpuPercent.Value < requested.MaxCpuPercent.Value)
                {
                    return false;
                }
                nextAllocated.MaxCpuPercent = requested.MaxCpuPercent.Value;
                nextAvailable.MaxCpuPercent -= requested.MaxCpuPercent.Value;
            }

            if (requested.MaxThreads.HasValue)
            {
                if (!current.MaxThreads.HasValue
                    || current.MaxThreads.Value < requested.MaxThreads.Value)
                {
                    return false;
                }
                nextAllocated.MaxThreads = requested.MaxThreads.Value;
                nextAvailable.MaxThreads -= requested.MaxThreads.Value;
            }

            if (requested.MaxConnections.HasValue)
            {
                if (!current.MaxConnections.HasValue
                    || current.MaxConnections.Value < requested.MaxConnections.Value)
                {
                    return false;
                }
                nextAllocated.MaxConnections = requested.MaxConnections.Value;
                nextAvailable.MaxConnections -= requested.MaxConnections.Value;
            }

            if (!TrySwapAvailable(current, nextAvailable))
            {
                continue;
            }

            allocated = nextAllocated;
            _allocatedResources[furnishingId] = new AllocatedResource
            {
                FurnishingId = furnishingId,
                Allocated = allocated,
                AllocatedAt = DateTime.UtcNow
            };

            return true;
        }
    }

    protected void ReleaseResources(string furnishingId)
    {
        if (!_allocatedResources.TryRemove(furnishingId, out var resource))
        {
            Observe(LogLevel.Warning, values: [("FurnishingId", furnishingId)]);
            return;
        }

        var allocated = resource.Allocated;

        while (true)
        {
            var current = ReadAvailable();
            var nextAvailable = new ResourceAllocation
            {
                MaxMemoryBytes = current.MaxMemoryBytes,
                MaxCpuPercent = current.MaxCpuPercent,
                MaxThreads = current.MaxThreads,
                MaxConnections = current.MaxConnections
            };

            if (allocated.MaxMemoryBytes.HasValue)
            {
                nextAvailable.MaxMemoryBytes = (current.MaxMemoryBytes ?? 0) + allocated.MaxMemoryBytes.Value;
            }

            if (allocated.MaxCpuPercent.HasValue)
            {
                nextAvailable.MaxCpuPercent = (current.MaxCpuPercent ?? 0) + allocated.MaxCpuPercent.Value;
            }

            if (allocated.MaxThreads.HasValue)
            {
                nextAvailable.MaxThreads = (current.MaxThreads ?? 0) + allocated.MaxThreads.Value;
            }

            if (allocated.MaxConnections.HasValue)
            {
                nextAvailable.MaxConnections = (current.MaxConnections ?? 0) + allocated.MaxConnections.Value;
            }

            if (TrySwapAvailable(current, nextAvailable))
            {
                return;
            }
        }
    }

    protected void LogAllocationFailure(IRequirement requirement)
    {
        Observe(LogLevel.Warning, values: [("RequirementId", requirement.RequirementId), ("RequiredType", requirement.RequiredType.Name)]);
    }

    public virtual void Dispose()
    {
    }
}

internal class AllocatedResource
{
    public string FurnishingId { get; set; } = string.Empty;
    public ResourceAllocation Allocated { get; set; } = new();
    public DateTime AllocatedAt { get; set; }
}
