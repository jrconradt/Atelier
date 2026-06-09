using Atelier.Framework.Attributes;

namespace Atelier.Framework.Attache;

[Contract("AttacheHealthReportInternal", Version = "1.0", Namespace = "Framework.Attache")]
public class AttacheHealthReport
{
    public required string InstanceId { get; set; }
    public required AttacheState State { get; set; }
    public required bool IsHealthy { get; set; }
    public required DateTime Timestamp { get; set; }
    public TimeSpan Uptime { get; set; }
    public AttacheResourceUsage ResourceUsage { get; set; } = new();
    public List<BoutiqueHealthReport> Boutiques { get; set; } = new();
    public List<string> Issues { get; set; } = new();
}

[Contract("BoutiqueHealthReportInternal", Version = "1.0", Namespace = "Framework.Attache")]
public class BoutiqueHealthReport
{
    public required string BoutiqueId { get; set; }
    public required string Name { get; set; }
    public required BoutiqueState State { get; set; }
    public required bool IsHealthy { get; set; }
    public int ActiveProducts { get; set; }
    public int TotalOfferings { get; set; }
    public BoutiqueResourceUsage ResourceUsage { get; set; } = new();
    public List<string> Issues { get; set; } = new();
}

public class AttacheResourceUsage
{
    public long MemoryUsageBytes { get; set; }
    public double CpuUsagePercent { get; set; }
    public int TotalBoutiques { get; set; }
    public int RunningBoutiques { get; set; }
    public int TotalProducts { get; set; }
    public int RunningProducts { get; set; }
    public int TotalOfferings { get; set; }
    public int RunningOfferings { get; set; }
}

public class BoutiqueResourceUsage
{
    public long MemoryUsageBytes { get; set; }
    public double CpuUsagePercent { get; set; }
    public int ActiveConnections { get; set; }
    public int TotalRequests { get; set; }
    public double AverageResponseTimeMs { get; set; }
}
