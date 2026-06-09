using Atelier.Framework.Primitives;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using Atelier.Framework.Attributes;
using Atelier.Framework.Observability;
using Atelier.Framework.Host.Execution;

namespace Atelier.Framework.Offering;

public interface IOfferingResourceMonitor : IDisposable
{
    public void UpdateResourceUsage(Atelier.Framework.Host.Execution.HostExecutionContext context);
    public void RemoveInstance(string instanceId);
    public bool IsWithinLimits(Atelier.Framework.Host.Execution.ResourceAllocation allocation);
    public ResourceViolation? DetectViolation(Atelier.Framework.Host.Execution.ResourceAllocation allocation);
    public ResourceAvailability GetAvailableResources();
}

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class OfferingResourceMonitor : IAtelier, IOfferingResourceMonitor
{
    private const int TOTAL_CPU_PERCENT = 100;
    private const double BYTES_PER_GIGABYTE = 1024.0 * 1024.0 * 1024.0;

    private readonly ConcurrentDictionary<string, ResourceUsageSnapshot> _usageSnapshots = new();

    public void UpdateResourceUsage(Atelier.Framework.Host.Execution.HostExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.ProcessId.HasValue)
        {
            try
            {
                using var process = Process.GetProcessById(context.ProcessId.Value);
                var cpuTime = process.TotalProcessorTime;
                var now = DateTime.UtcNow;

                _usageSnapshots.TryGetValue(context.InstanceId, out var previous);

                _usageSnapshots[context.InstanceId] = new ResourceUsageSnapshot
                {
                    InstanceId = context.InstanceId,
                    MemoryBytes = process.WorkingSet64,
                    CpuPercent = CalculateCpuPercent(previous, cpuTime, now),
                    CpuTime = cpuTime,
                    ThreadCount = process.Threads.Count,
                    LastUpdated = now
                };
            }
            catch (Exception ex) when (ex is ArgumentException
                || ex is InvalidOperationException
                || ex is Win32Exception)
            {
                Observe(LogLevel.Warning, ex, values: [("InstanceId", context.InstanceId), ("ProcessId", context.ProcessId.Value)]);
            }
        }
    }

    public void RemoveInstance(string instanceId)
    {
        ArgumentNullException.ThrowIfNull(instanceId);

        _usageSnapshots.TryRemove(instanceId, out _);
    }

    public bool IsWithinLimits(Atelier.Framework.Host.Execution.ResourceAllocation allocation)
    {
        ArgumentNullException.ThrowIfNull(allocation);

        var available = GetAvailableResources();

        if (allocation.MaxMemoryBytes.HasValue)
        {
            var requiredMemoryGB = allocation.MaxMemoryBytes.Value / BYTES_PER_GIGABYTE;
            if (requiredMemoryGB > available.AvailableMemoryGB)
            {
                return false;
            }
        }

        if (allocation.MaxCpuPercent.HasValue)
        {
            if (allocation.MaxCpuPercent.Value > available.AvailableCpuPercent)
            {
                return false;
            }
        }

        if (allocation.MaxThreads.HasValue)
        {
            if (allocation.MaxThreads.Value > available.AvailableThreads)
            {
                return false;
            }
        }

        return true;
    }

    public ResourceViolation? DetectViolation(Atelier.Framework.Host.Execution.ResourceAllocation allocation)
    {
        ArgumentNullException.ThrowIfNull(allocation);

        var available = GetAvailableResources();

        if (allocation.MaxMemoryBytes.HasValue)
        {
            var requiredMemoryGB = allocation.MaxMemoryBytes.Value / BYTES_PER_GIGABYTE;
            if (requiredMemoryGB > available.AvailableMemoryGB)
            {
                return new ResourceViolation
                {
                    ResourceType = "Memory",
                    Limit = available.AvailableMemoryGB,
                    Current = requiredMemoryGB,
                    DetectedAt = DateTime.UtcNow
                };
            }
        }

        if (allocation.MaxCpuPercent.HasValue)
        {
            if (allocation.MaxCpuPercent.Value > available.AvailableCpuPercent)
            {
                return new ResourceViolation
                {
                    ResourceType = "CPU",
                    Limit = available.AvailableCpuPercent,
                    Current = allocation.MaxCpuPercent.Value,
                    DetectedAt = DateTime.UtcNow
                };
            }
        }

        if (allocation.MaxThreads.HasValue)
        {
            if (allocation.MaxThreads.Value > available.AvailableThreads)
            {
                return new ResourceViolation
                {
                    ResourceType = "Threads",
                    Limit = available.AvailableThreads,
                    Current = allocation.MaxThreads.Value,
                    DetectedAt = DateTime.UtcNow
                };
            }
        }

        return null;
    }

    public ResourceAvailability GetAvailableResources()
    {
        var totalMemoryBytes = GetSystemMemoryLimitBytes();

        long allocatedMemory = 0;
        var allocatedCpu = 0;
        var allocatedThreads = 0;

        foreach (var snapshot in _usageSnapshots.Values)
        {
            allocatedMemory += snapshot.MemoryBytes;
            allocatedCpu += snapshot.CpuPercent;
            allocatedThreads += snapshot.ThreadCount;
        }

        var availableMemoryBytes = Math.Max(0, totalMemoryBytes - allocatedMemory);
        var availableCpuPercent = Math.Max(0, TOTAL_CPU_PERCENT - allocatedCpu);
        var totalThreads = GetSystemThreadLimit();

        return new ResourceAvailability
        {
            TotalMemoryGB = totalMemoryBytes / BYTES_PER_GIGABYTE,
            AvailableMemoryGB = availableMemoryBytes / BYTES_PER_GIGABYTE,
            AllocatedMemoryGB = allocatedMemory / BYTES_PER_GIGABYTE,
            TotalCpuPercent = TOTAL_CPU_PERCENT,
            AvailableCpuPercent = availableCpuPercent,
            AllocatedCpuPercent = allocatedCpu,
            AvailableThreads = Math.Max(0, totalThreads - allocatedThreads),
            ProcessorCount = Environment.ProcessorCount
        };
    }

    private static long GetSystemMemoryLimitBytes()
    {
        var gcMemoryInfo = GC.GetGCMemoryInfo();
        if (gcMemoryInfo.TotalAvailableMemoryBytes > 0)
        {
            return gcMemoryInfo.TotalAvailableMemoryBytes;
        }

        return Environment.WorkingSet;
    }

    private static int GetSystemThreadLimit()
    {
        ThreadPool.GetMaxThreads(out var maxWorkerThreads, out _);
        return maxWorkerThreads;
    }

    private static int CalculateCpuPercent(
        ResourceUsageSnapshot? previous,
        TimeSpan currentCpuTime,
        DateTime now)
    {
        if (previous is null)
        {
            return 0;
        }

        var cpuDelta = currentCpuTime - previous.CpuTime;
        var wallDelta = now - previous.LastUpdated;

        if (cpuDelta <= TimeSpan.Zero
            || wallDelta <= TimeSpan.Zero
            || Environment.ProcessorCount <= 0)
        {
            return 0;
        }

        var percent = cpuDelta.TotalMilliseconds
            / (wallDelta.TotalMilliseconds * Environment.ProcessorCount)
            * TOTAL_CPU_PERCENT;

        return Math.Clamp((int)Math.Round(percent), 0, TOTAL_CPU_PERCENT);
    }

    public void Dispose()
    {
        _usageSnapshots.Clear();
    }
}

public class ResourceUsageSnapshot
{
    public string InstanceId { get; set; } = string.Empty;
    public long MemoryBytes { get; set; }
    public int CpuPercent { get; set; }
    public TimeSpan CpuTime { get; set; }
    public int ThreadCount { get; set; }
    public DateTime LastUpdated { get; set; }
}

public class ResourceAvailability
{
    public double TotalMemoryGB { get; set; }
    public double AvailableMemoryGB { get; set; }
    public double AllocatedMemoryGB { get; set; }
    public int TotalCpuPercent { get; set; }
    public int AvailableCpuPercent { get; set; }
    public int AllocatedCpuPercent { get; set; }
    public int AvailableThreads { get; set; }
    public int ProcessorCount { get; set; }
}

public class ResourceViolation
{
    public string ResourceType { get; set; } = string.Empty;
    public double Limit { get; set; }
    public double Current { get; set; }
    public DateTime DetectedAt { get; set; }
    public string Message => $"{ResourceType} limit exceeded: {Current} > {Limit}";
}
