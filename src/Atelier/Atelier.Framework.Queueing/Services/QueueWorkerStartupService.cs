using Atelier.Framework.Observability;
using Atelier.Framework.Queueing.Attributes;
using Atelier.Framework.Queueing.Extensions;
using Atelier.Framework.Queueing.Orchestration;
using Atelier.Framework.Queueing.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Atelier.Framework.Queueing.Services;

public class QueueWorkerStartupService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly QueueWorkerRegistry _registry;
    private readonly ILogger? _logger;
    private readonly List<IQueueWorker> _startedWorkers = new();
    private readonly List<IServiceScope> _workerScopes = new();

    public QueueWorkerStartupService(
        IServiceProvider serviceProvider,
        QueueWorkerRegistry registry)
    {
        _serviceProvider = serviceProvider;
        _registry = registry;
        _logger = serviceProvider.GetService<ILogger>();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var loadError in _registry.GetLoadErrors())
        {
            _logger?.WithMessage("Assembly load error during queue worker discovery")
                   .WithValue("Assembly", loadError.AssemblyName)
                   .WithError(loadError.Exception)
                   .WithLevel(LogLevel.Warning)
                   .Log();
        }

        var registrations = _registry.GetRegistrations();

        if (registrations.Count == 0)
        {
            _logger?.WithMessage("No queue workers discovered to start")
                   .WithLevel(LogLevel.Information)
                   .Log();
            return;
        }

        _logger?.WithMessage("Starting queue worker discovery service")
               .WithValue("WorkerCount", registrations.Count)
               .WithLevel(LogLevel.Information)
               .Log();

        var queueManager = _serviceProvider.GetService<IQueueManager>();
        if (queueManager == null)
        {
            _logger?.WithMessage("IQueueManager not available, skipping worker registration")
                   .WithLevel(LogLevel.Warning)
                   .Log();
            return;
        }

        foreach (var registration in registrations)
        {
            try
            {
                var worker = ResolveWorker(registration);
                if (worker == null)
                {
                    _logger?.WithMessage("Failed to resolve queue worker")
                           .WithValue("WorkerType", registration.WorkerType.Name)
                           .WithLevel(LogLevel.Warning)
                           .Log();
                    continue;
                }

                var registerResult = await queueManager.RegisterWorkerAsync(worker, cancellationToken).ConfigureAwait(false);
                if (!registerResult.IsSuccess)
                {
                    _logger?.WithMessage("Failed to register queue worker with QueueManager")
                           .WithValue("WorkerName", worker.WorkerName)
                           .WithValue("WorkerType", registration.WorkerType.Name)
                           .WithLevel(LogLevel.Warning)
                           .Log();
                    continue;
                }

                var shouldAutoStart = registration.LifecycleAttribute?.AutoStart ?? true;
                if (shouldAutoStart)
                {
                    var startResult = await worker.StartAsync(cancellationToken).ConfigureAwait(false);
                    if (!startResult.IsSuccess)
                    {
                        _logger?.WithMessage("Failed to start queue worker")
                               .WithValue("WorkerName", worker.WorkerName)
                               .WithLevel(LogLevel.Warning)
                               .Log();
                        continue;
                    }

                    _startedWorkers.Add(worker);

                    _logger?.WithMessage("Queue worker started successfully")
                           .WithValue("WorkerName", worker.WorkerName)
                           .WithValue("WorkerType", registration.WorkerType.Name)
                           .WithValue("QueueCount", worker.QueueConfigurations.Count())
                           .WithLevel(LogLevel.Information)
                           .Log();
                }
                else
                {
                    _logger?.WithMessage("Queue worker registered but not auto-started")
                           .WithValue("WorkerName", worker.WorkerName)
                           .WithValue("WorkerType", registration.WorkerType.Name)
                           .WithLevel(LogLevel.Information)
                           .Log();
                }
            }
            catch (Exception ex)
            {
                _logger?.WithMessage("Exception while starting queue worker")
                       .WithValue("WorkerType", registration.WorkerType.Name)
                       .WithError(ex)
                       .WithLevel(LogLevel.Error)
                       .Log();
            }
        }

        _logger?.WithMessage("Queue worker startup completed")
               .WithValue("StartedCount", _startedWorkers.Count)
               .WithValue("TotalCount", registrations.Count)
               .WithLevel(LogLevel.Information)
               .Log();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger?.WithMessage("Stopping queue workers")
               .WithValue("WorkerCount", _startedWorkers.Count)
               .WithLevel(LogLevel.Information)
               .Log();

        var queueManager = _serviceProvider.GetService<IQueueManager>();

        foreach (var worker in _startedWorkers)
        {
            try
            {
                var stopResult = await worker.StopAsync(cancellationToken).ConfigureAwait(false);
                if (!stopResult.IsSuccess)
                {
                    _logger?.WithMessage("Failed to stop queue worker")
                           .WithValue("WorkerName", worker.WorkerName)
                           .WithLevel(LogLevel.Warning)
                           .Log();
                }
                else
                {
                    _logger?.WithMessage("Queue worker stopped successfully")
                           .WithValue("WorkerName", worker.WorkerName)
                           .WithLevel(LogLevel.Debug)
                           .Log();
                }

                if (queueManager != null)
                {
                    await queueManager.UnregisterWorkerAsync(worker.WorkerName, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger?.WithMessage("Exception while stopping queue worker")
                       .WithValue("WorkerName", worker.WorkerName)
                       .WithError(ex)
                       .WithLevel(LogLevel.Error)
                       .Log();
            }
        }

        _startedWorkers.Clear();

        foreach (var scope in _workerScopes)
        {
            scope.Dispose();
        }

        _workerScopes.Clear();

        _logger?.WithMessage("Queue worker shutdown completed")
               .WithLevel(LogLevel.Information)
               .Log();
    }

    private IQueueWorker? ResolveWorker(QueueWorkerRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        var lifecycle = registration.LifecycleAttribute?.Lifecycle ?? WorkerLifecycle.Singleton;

        if (lifecycle == WorkerLifecycle.Scoped || lifecycle == WorkerLifecycle.Transient)
        {
            var scope = _serviceProvider.CreateScope();
            var scopedWorker = scope.ServiceProvider.GetService(registration.WorkerType) as IQueueWorker;
            if (scopedWorker == null)
            {
                scope.Dispose();
                return null;
            }

            _workerScopes.Add(scope);
            return scopedWorker;
        }

        return _serviceProvider.GetService(registration.WorkerType) as IQueueWorker;
    }
}
