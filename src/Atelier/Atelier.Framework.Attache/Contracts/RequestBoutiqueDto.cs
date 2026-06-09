using System.ComponentModel.DataAnnotations;
using Atelier.Framework.Attributes;

namespace Atelier.Framework.Attache.Contracts;

[Contract("RequestBoutique", Version = "1.0")]
public class RequestBoutiqueDto
{
    [Required]
    public required List<string> RequiredProducts { get; init; }

    [Required]
    public required CapabilitiesDto Capabilities { get; init; }

    [Required]
    public required RuntimeConfigDto RuntimeConfig { get; init; }

    public bool AutoDeploy { get; init; } = true;

    public Dictionary<string, string>? EnvironmentVariables { get; init; }
}

[Contract("Capabilities", Version = "1.0")]
public class CapabilitiesDto
{
    public bool Rest { get; init; }
    public bool Grpc { get; init; }
    public bool WebSocket { get; init; }
}

[Contract("RuntimeConfig", Version = "1.0")]
public class RuntimeConfigDto
{
    public int? HttpPort { get; init; }
    public int? GrpcPort { get; init; }

    [Range(1, 256)]
    public long MaxMemoryGB { get; init; } = 16;

    [Range(1, 100)]
    public int MaxCpuPercent { get; init; } = 80;
}
