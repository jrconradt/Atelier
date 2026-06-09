using Atelier.Framework.Offering.Product;
using Atelier.Framework.Outcomes;

namespace Atelier.Framework.Attache;

public interface IBoutique
{
    public string BoutiqueId { get; }
    public string Name { get; }
    public BoutiqueState State { get; }
    public BoutiqueManifest Manifest { get; }
    public IReadOnlyDictionary<string, ProductBase> Products { get; }

    public Task<Outcome> StartAsync(CancellationToken cancellationToken = default);
    public Task<Outcome> StopAsync(CancellationToken cancellationToken = default);

    public Task<Outcome<string>> AddProductAsync(
        ProductManifest manifest,
        CancellationToken cancellationToken = default);

    public Task<Outcome> RemoveProductAsync(
        string productId,
        CancellationToken cancellationToken = default);

    public Task<Outcome<BoutiqueHealthReport>> GetHealthReportAsync(
        CancellationToken cancellationToken = default);
}

public enum BoutiqueState
{
    Created,
    Starting,
    Running,
    Stopping,
    Stopped,
    Failed
}
