using Atelier.Framework.Attache.Lifecycle;
using Microsoft.Extensions.Hosting;

namespace Atelier.Framework.Attache.Hosting;

internal sealed class BoutiqueDrainService : IHostedLifecycleService
{
    private readonly IServiceProvider _services;

    public BoutiqueDrainService(IServiceProvider services)
    {
        _services = services;
    }

    public Task StoppingAsync(CancellationToken cancellationToken) => _services.DrainForShutdownAsync();

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
