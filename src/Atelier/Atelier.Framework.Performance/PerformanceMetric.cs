using Atelier.Framework.Attributes;

namespace Atelier.Framework.Performance;

[Contract("PerformanceMetric", Version = "1.0")]
public class PerformanceMetric
{
    public string MetricId { get; set; } = string.Empty;
    public string Component { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public MetricType Type { get; set; }
    public double Value { get; set; }
    public string Unit { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Tags { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public enum MetricType
{
    Latency,
    Throughput,
    Memory,
    CPU,
    DiskIO,
    NetworkIO,
    ErrorRate,
    CacheHitRate,
    QueueDepth,
    Custom
}

[Contract("MetricQuery", Version = "1.0")]
public class MetricQuery
{
    public string? Component { get; set; }
    public string? Operation { get; set; }
    public MetricType? Type { get; set; }
    public DateTime? FromTime { get; set; }
    public DateTime? ToTime { get; set; }
}

[Contract("PerformanceSnapshot", Version = "1.0")]
public class PerformanceSnapshot
{
    public string SnapshotId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public SystemMetrics System { get; set; } = new();
    public Dictionary<string, ComponentMetrics> Components { get; set; } = new();
    public List<PerformanceAlert> Alerts { get; set; } = new();
}

[Contract("SystemMetrics", Version = "1.0")]
public class SystemMetrics
{
    public double CpuUsagePercent { get; set; }
    public long MemoryUsedBytes { get; set; }
    public long MemoryAvailableBytes { get; set; }
    public double MemoryUsagePercent { get; set; }
    public long DiskReadBytesPerSec { get; set; }
    public long DiskWriteBytesPerSec { get; set; }
    public long NetworkSentBytesPerSec { get; set; }
    public long NetworkReceivedBytesPerSec { get; set; }
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }
    public bool DiskMetricsAvailable { get; set; }
    public bool NetworkMetricsAvailable { get; set; }
}

[Contract("ComponentMetrics", Version = "1.0")]
public class ComponentMetrics
{
    public string ComponentName { get; set; } = string.Empty;
    public double AverageLatencyMs { get; set; }
    public double P50LatencyMs { get; set; }
    public double P95LatencyMs { get; set; }
    public double P99LatencyMs { get; set; }
    public long TotalOperations { get; set; }
    public double OperationsPerSecond { get; set; }
    public long ErrorCount { get; set; }
    public double ErrorRate { get; set; }
    public long MemoryAllocatedBytes { get; set; }
    public Dictionary<string, double> CustomMetrics { get; set; } = new();
}

[Contract("PerformanceAlert", Version = "1.0")]
public class PerformanceAlert
{
    public string AlertId { get; set; } = string.Empty;
    public AlertSeverity Severity { get; set; }
    public string Component { get; set; } = string.Empty;
    public string Metric { get; set; } = string.Empty;
    public double Threshold { get; set; }
    public double ActualValue { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public enum AlertSeverity
{
    Info,
    Warning,
    Critical
}

[Contract("PerformanceBaseline", Version = "1.0")]
public class PerformanceBaseline
{
    public string BaselineId { get; set; } = string.Empty;
    public string Component { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public double BaselineValue { get; set; }
    public double StandardDeviation { get; set; }
    public double AcceptableDeviationPercent { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int SampleCount { get; set; }
}
