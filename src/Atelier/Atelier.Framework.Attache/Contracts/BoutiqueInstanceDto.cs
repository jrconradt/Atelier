using Atelier.Framework.Attributes;

namespace Atelier.Framework.Attache.Contracts;

[Contract("BoutiqueInstance", Version = "1.0")]
public class BoutiqueInstanceDto
{
    public required string BoutiqueId { get; init; }
    public required string Status { get; init; }
    public string? HttpEndpoint { get; init; }
    public string? GrpcEndpoint { get; init; }
    public required string ArtifactsPath { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? TerminatedAt { get; init; }
    public int AssemblyCount { get; init; }
    public int TypeCount { get; init; }
}

[Contract("BoutiqueHealth", Version = "1.0")]
public class BoutiqueHealthDto
{
    public required string BoutiqueId { get; init; }
    public required string State { get; init; }
    public string? Message { get; init; }
    public DateTime LastCheckAt { get; init; }
    public Dictionary<string, string> Details { get; init; } = new();
}

[Contract("ScaleBoutique", Version = "1.0")]
public class ScaleBoutiqueDto
{
    public required string BoutiqueId { get; init; }
    public required int InstanceCount { get; init; }
}
