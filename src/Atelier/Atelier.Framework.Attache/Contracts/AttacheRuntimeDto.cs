using Atelier.Framework.Attributes;

namespace Atelier.Framework.Attache.Contracts;

[Contract("AttacheRuntimeStatus", Version = "1.0")]
public class AttacheRuntimeStatusDto
{
    public required string State { get; init; }
    public int BoutiqueCount { get; init; }
    public required AttacheConfigurationDto Configuration { get; init; }
}

[Contract("AttacheConfiguration", Version = "1.0")]
public class AttacheConfigurationDto
{
    public int MaxBoutiques { get; init; }
    public bool AutoStartBoutiques { get; init; }
}

[Contract("AttacheHealthReport", Version = "1.0")]
public class AttacheHealthReportDto
{
    public required string OverallHealth { get; init; }
    public DateTime Timestamp { get; init; }
    public List<BoutiqueHealthSummaryDto> BoutiqueHealths { get; init; } = new();
}

[Contract("BoutiqueHealthSummary", Version = "1.0")]
public class BoutiqueHealthSummaryDto
{
    public required string BoutiqueName { get; init; }
    public required string Health { get; init; }
    public string? Message { get; init; }
}
