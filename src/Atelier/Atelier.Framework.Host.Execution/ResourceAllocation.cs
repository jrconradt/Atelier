namespace Atelier.Framework.Host.Execution;

public class ResourceAllocation
{
    public long? MaxMemoryBytes { get; set; }
    public int? MaxMemoryMB
    {
        get => MaxMemoryBytes.HasValue ? (int?)(MaxMemoryBytes.Value / 1024 / 1024) : null;
        set => MaxMemoryBytes = value.HasValue ? value.Value * 1024 * 1024 : null;
    }
    public int? MaxCpuPercent { get; set; }
    public int? MaxThreads { get; set; }
    public int? MaxConnections { get; set; }
}
