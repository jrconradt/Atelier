using Atelier.Framework.Observability;
using Atelier.Framework.Requisitions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Atelier.Framework.Attache.Lifecycle;

public partial class BoutiqueStartupHostedService : IHostedService, IAtelier
{
    [Requisite] private readonly IServiceProvider _serviceProvider = null!;
    [Requisite] private readonly IHostApplicationLifetime _lifetime = null!;

    private readonly StartupConfiguration _configuration = new();
    private readonly StartupHolder _holder = new();

    private BoutiqueManifest _manifest => _configuration.Manifest;
    private BoutiqueStartupState _state => _configuration.State;

    private sealed class StartupConfiguration
    {
        public BoutiqueManifest Manifest { get; set; } = null!;
        public BoutiqueStartupState State { get; set; } = null!;
    }

    private sealed class StartupHolder
    {
        public Boutique? Boutique;
    }

    public BoutiqueStartupHostedService Configure(BoutiqueManifest manifest,
                                                  BoutiqueStartupState state)
    {
        _configuration.Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        _configuration.State = state ?? throw new ArgumentNullException(nameof(state));
        return this;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var boutique = _serviceProvider.GetRequiredService<Boutique>().Configure(_manifest);
        _holder.Boutique = boutique;

        var startResult = await boutique.StartAsync(cancellationToken).ConfigureAwait(false);
        _state.SetResult(startResult);

        if (startResult.IsSuccess)
        {
            Observe(LogLevel.Information, values: [("Event", "BoutiqueStarted"), ("ProductCount", boutique.Products.Count)]);
            return;
        }

        Environment.ExitCode = 1;
        Observe(LogLevel.Error, values: [("Event", "BoutiqueStartupFailed")]);

        _lifetime.StopApplication();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _state.BeginDraining();

        var boutique = _holder.Boutique;
        if (boutique is null)
        {
            return;
        }

        var stopResult = await boutique.StopAsync(cancellationToken).ConfigureAwait(false);
        if (!stopResult.IsSuccess)
        {
            Observe(LogLevel.Warning, values: [("Event", "BoutiqueShutdownReported")]);
        }
    }
}
