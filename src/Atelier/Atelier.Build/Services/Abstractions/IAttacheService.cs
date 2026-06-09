using Atelier.Build.Analysis;
using Atelier.Build.Discovery;

namespace Atelier.Build.Services.Abstractions;

public interface IAttacheService
{
        public Task<BoutiqueInstance?> RequestBoutiqueAsync(
        BoutiqueRequest request,
        CancellationToken cancellationToken = default);

        public Task<bool> TerminateBoutiqueAsync(
        string boutiqueId,
        CancellationToken cancellationToken = default);

        public Task<bool> StartBoutiqueAsync(
        string boutiqueId,
        CancellationToken cancellationToken = default);

        public Task<bool> StopBoutiqueAsync(
        string boutiqueId,
        CancellationToken cancellationToken = default);

        public IReadOnlyList<BoutiqueInstance> ListBoutiques();

        public BoutiqueInstance? GetBoutique(string boutiqueId);

        public BoutiqueMetrics? GetBoutiqueMetrics(string boutiqueId);

        public Task<BoutiqueHealthStatus> GetBoutiqueHealthAsync(
        string boutiqueId,
        CancellationToken cancellationToken = default);
}

public record BoutiqueHealthStatus
{
    public required string BoutiqueId { get; init; }
    public HealthState State { get; init; }
    public string? Message { get; init; }
    public DateTime LastCheckAt { get; init; }
    public Dictionary<string, string> Details { get; init; } = new();
}

public enum HealthState
{
    Healthy,
    Degraded,
    Unhealthy,
    Unknown
}
