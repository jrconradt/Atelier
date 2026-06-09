using Atelier.Framework.Primitives;
using System.Collections.Concurrent;
using Atelier.Framework.Infrastructure;
using Atelier.Framework.Attributes;
using Atelier.Framework.Infrastructure.Operation;
using Atelier.Framework.Observability;
using Atelier.Framework.Requisitions;
using Prometheus;

namespace Atelier.Framework.Performance;

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class PrometheusMetricsExporter : IAtelier, Microsoft.Extensions.Hosting.IHostedService
{
    private static readonly TimeSpan DefaultUpdateInterval = TimeSpan.FromSeconds(15);

    [Requisite] protected readonly IPerformanceMonitor _monitor = null!;
    [Requisite] protected readonly IPerformanceProfiler _profiler = null!;

    Task Microsoft.Extensions.Hosting.IHostedService.StartAsync(CancellationToken cancellationToken)
    {
        Initialize(DefaultUpdateInterval);
        return Task.CompletedTask;
    }

    Task Microsoft.Extensions.Hosting.IHostedService.StopAsync(CancellationToken cancellationToken)
    {
        _state.Timer?.Dispose();
        _state.Timer = null;
        return Task.CompletedTask;
    }
    private readonly ConcurrentDictionary<string, Gauge> _gauges = new();
    private readonly ExporterState _state = new();

    private sealed class ExporterState
    {
        public Timer? Timer;
        public int Initialized;
        public int UpdateInFlight;
    }

    private static readonly Gauge CpuUsage = Metrics.CreateGauge(
        "atelier_system_cpu_usage_percent",
        "Current CPU usage percentage");

    private static readonly Gauge MemoryUsage = Metrics.CreateGauge(
        "atelier_system_memory_usage_bytes",
        "Current memory usage in bytes");

    private static readonly Gauge MemoryAvailable = Metrics.CreateGauge(
        "atelier_system_memory_available_bytes",
        "Available memory in bytes");

    private static readonly Gauge ThreadCount = Metrics.CreateGauge(
        "atelier_system_thread_count",
        "Number of active threads");

    private static readonly Gauge HandleCount = Metrics.CreateGauge(
        "atelier_system_handle_count",
        "Number of handles");

    private static readonly Gauge DiskReadRate = Metrics.CreateGauge(
        "atelier_system_disk_read_bytes_per_sec",
        "Disk read rate in bytes per second");

    private static readonly Gauge DiskWriteRate = Metrics.CreateGauge(
        "atelier_system_disk_write_bytes_per_sec",
        "Disk write rate in bytes per second");

    private static readonly Gauge NetworkSentRate = Metrics.CreateGauge(
        "atelier_system_network_sent_bytes_per_sec",
        "Network sent rate in bytes per second");

    private static readonly Gauge NetworkReceivedRate = Metrics.CreateGauge(
        "atelier_system_network_received_bytes_per_sec",
        "Network received rate in bytes per second");

    public void Initialize(TimeSpan updateInterval)
    {
        if (Interlocked.CompareExchange(ref _state.Initialized, 1, 0) == 1)
        {
            return;
        }

        Observe(LogLevel.Information, values: [("UpdateInterval", updateInterval.TotalSeconds)]);

        _state.Timer = new Timer(
            _ => _ = UpdateMetricsAsync(),
            null,
            TimeSpan.Zero,
            updateInterval);
    }

    private async Task UpdateMetricsAsync()
    {
        if (Interlocked.CompareExchange(ref _state.UpdateInFlight, 1, 0) == 1)
        {
            return;
        }

        try
        {
            if (_profiler == null || _monitor == null)
            {
                return;
            }

            await UpdateSystemMetricsAsync().ConfigureAwait(false);
            await UpdateComponentMetricsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Observe(LogLevel.Warning, ex);
        }
        finally
        {
            Interlocked.Exchange(ref _state.UpdateInFlight, 0);
        }
    }

    private async Task UpdateSystemMetricsAsync()
    {
        var snapshotResult = await _profiler.CaptureSnapshotAsync().ConfigureAwait(false);

        if (!snapshotResult.IsSuccess)
        {
            return;
        }

        var snapshot = snapshotResult.Data!;
        var system = snapshot.System;

        CpuUsage.Set(system.CpuUsagePercent);
        MemoryUsage.Set(system.MemoryUsedBytes);
        MemoryAvailable.Set(system.MemoryAvailableBytes);
        ThreadCount.Set(system.ThreadCount);
        HandleCount.Set(system.HandleCount);
        DiskReadRate.Set(system.DiskReadBytesPerSec);
        DiskWriteRate.Set(system.DiskWriteBytesPerSec);
        NetworkSentRate.Set(system.NetworkSentBytesPerSec);
        NetworkReceivedRate.Set(system.NetworkReceivedBytesPerSec);
    }

    private async Task UpdateComponentMetricsAsync()
    {
        var metricsResult = await _monitor.GetAllComponentMetricsAsync(
            window: TimeSpan.FromMinutes(5)).ConfigureAwait(false);

        if (!metricsResult.IsSuccess)
        {
            return;
        }

        var components = metricsResult.Data!;

        foreach (var (componentName, metrics) in components)
        {
            UpdateComponentLatency(componentName, metrics);
            UpdateComponentThroughput(componentName, metrics);
            UpdateComponentErrorRate(componentName, metrics);
            UpdateComponentMemory(componentName, metrics);
        }
    }

    private void UpdateComponentLatency(string component, ComponentMetrics metrics)
    {
        var avgGaugeKey = $"latency_avg_{component}";
        var p50GaugeKey = $"latency_p50_{component}";
        var p95GaugeKey = $"latency_p95_{component}";
        var p99GaugeKey = $"latency_p99_{component}";

        if (!_gauges.TryGetValue(avgGaugeKey, out var avgGauge))
        {
            avgGauge = Metrics.CreateGauge(
                $"atelier_component_latency_avg_ms",
                "Component average latency in milliseconds",
                new GaugeConfiguration
                {
                    LabelNames = new[] { "component" }
                });
            _gauges[avgGaugeKey] = avgGauge;
        }

        if (!_gauges.TryGetValue(p50GaugeKey, out var p50Gauge))
        {
            p50Gauge = Metrics.CreateGauge(
                $"atelier_component_latency_p50_ms",
                "Component P50 latency in milliseconds",
                new GaugeConfiguration
                {
                    LabelNames = new[] { "component" }
                });
            _gauges[p50GaugeKey] = p50Gauge;
        }

        if (!_gauges.TryGetValue(p95GaugeKey, out var p95Gauge))
        {
            p95Gauge = Metrics.CreateGauge(
                $"atelier_component_latency_p95_ms",
                "Component P95 latency in milliseconds",
                new GaugeConfiguration
                {
                    LabelNames = new[] { "component" }
                });
            _gauges[p95GaugeKey] = p95Gauge;
        }

        if (!_gauges.TryGetValue(p99GaugeKey, out var p99Gauge))
        {
            p99Gauge = Metrics.CreateGauge(
                $"atelier_component_latency_p99_ms",
                "Component P99 latency in milliseconds",
                new GaugeConfiguration
                {
                    LabelNames = new[] { "component" }
                });
            _gauges[p99GaugeKey] = p99Gauge;
        }

        avgGauge.WithLabels(component).Set(metrics.AverageLatencyMs);
        p50Gauge.WithLabels(component).Set(metrics.P50LatencyMs);
        p95Gauge.WithLabels(component).Set(metrics.P95LatencyMs);
        p99Gauge.WithLabels(component).Set(metrics.P99LatencyMs);
    }

    private void UpdateComponentThroughput(string component, ComponentMetrics metrics)
    {
        var gaugeKey = $"throughput_{component}";

        if (!_gauges.TryGetValue(gaugeKey, out var gauge))
        {
            gauge = Metrics.CreateGauge(
                $"atelier_component_throughput_ops_per_sec",
                "Component throughput in operations per second",
                new GaugeConfiguration
                {
                    LabelNames = new[] { "component" }
                });
            _gauges[gaugeKey] = gauge;
        }

        gauge.WithLabels(component).Set(metrics.OperationsPerSecond);
    }

    private void UpdateComponentErrorRate(string component, ComponentMetrics metrics)
    {
        var rateGaugeKey = $"error_rate_{component}";
        var countGaugeKey = $"error_count_{component}";

        if (!_gauges.TryGetValue(rateGaugeKey, out var rateGauge))
        {
            rateGauge = Metrics.CreateGauge(
                $"atelier_component_error_rate",
                "Component error rate (0-1)",
                new GaugeConfiguration
                {
                    LabelNames = new[] { "component" }
                });
            _gauges[rateGaugeKey] = rateGauge;
        }

        if (!_gauges.TryGetValue(countGaugeKey, out var countGauge))
        {
            countGauge = Metrics.CreateGauge(
                $"atelier_component_error_count",
                "Component error count",
                new GaugeConfiguration
                {
                    LabelNames = new[] { "component" }
                });
            _gauges[countGaugeKey] = countGauge;
        }

        rateGauge.WithLabels(component).Set(metrics.ErrorRate);
        countGauge.WithLabels(component).Set(metrics.ErrorCount);
    }

    private void UpdateComponentMemory(string component, ComponentMetrics metrics)
    {
        var gaugeKey = $"memory_{component}";

        if (!_gauges.TryGetValue(gaugeKey, out var gauge))
        {
            gauge = Metrics.CreateGauge(
                $"atelier_component_memory_allocated_bytes",
                "Component memory allocated in bytes",
                new GaugeConfiguration
                {
                    LabelNames = new[] { "component" }
                });
            _gauges[gaugeKey] = gauge;
        }

        gauge.WithLabels(component).Set(metrics.MemoryAllocatedBytes);
    }
}
