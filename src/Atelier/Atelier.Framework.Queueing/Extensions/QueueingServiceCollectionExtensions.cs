using System.Reflection;
using Atelier.Framework.Attributes;
using Atelier.Framework.Queueing.Attributes;
using Atelier.Framework.Queueing.Services;
using Atelier.Framework.Queueing.Workers;
using Microsoft.Extensions.DependencyInjection;

namespace Atelier.Framework.Queueing.Extensions;

public static class QueueingServiceCollectionExtensions
{
    public static IServiceCollection AddQueueWorkers(
        this IServiceCollection services,
        Action<QueueWorkerDiscoveryOptions>? configure = null)
    {
        var options = new QueueWorkerDiscoveryOptions();
        configure?.Invoke(options);

        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Concat(options.AdditionalAssemblies)
            .Distinct()
            .ToList();

        var discoveredWorkers = new List<QueueWorkerRegistration>();
        var loadErrors = new List<QueueWorkerLoadError>();

        foreach (var assembly in assemblies)
        {
            try
            {
                Type[] candidateTypes;
                try
                {
                    candidateTypes = assembly.GetExportedTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    if (options.ThrowOnLoadErrors)
                    {
                        throw;
                    }

                    loadErrors.Add(new QueueWorkerLoadError(assembly.FullName ?? assembly.GetName().Name ?? "unknown", ex));
                    candidateTypes = ex.Types.Where(t => t != null).Select(t => t!).ToArray();
                }

                var workerTypes = candidateTypes
                    .Where(t => t.GetCustomAttribute<QueueWorkerAttribute>() != null &&
                                !t.IsAbstract &&
                                t.IsClass &&
                                typeof(IQueueWorker).IsAssignableFrom(t));

                foreach (var workerType in workerTypes)
                {
                    var attributes = workerType.GetCustomAttributes<QueueWorkerAttribute>().ToList();
                    var lifecycleAttr = workerType.GetCustomAttribute<QueueWorkerLifecycleAttribute>();

                    if (attributes.Count == 0)
                    {
                        continue;
                    }

                    var registration = new QueueWorkerRegistration
                    {
                        WorkerType = workerType,
                        QueueAttributes = attributes,
                        LifecycleAttribute = lifecycleAttr
                    };

                    var lifetime = DetermineServiceLifetime(lifecycleAttr);
                    services.Add(new ServiceDescriptor(workerType, workerType, lifetime));

                    if (workerType.GetInterfaces().FirstOrDefault(i => i != typeof(IQueueWorker)) is Type serviceInterface)
                    {
                        services.Add(new ServiceDescriptor(serviceInterface, sp => sp.GetRequiredService(workerType), lifetime));
                    }

                    discoveredWorkers.Add(registration);
                }
            }
            catch (ReflectionTypeLoadException ex)
            {
                if (options.ThrowOnLoadErrors)
                {
                    throw;
                }

                loadErrors.Add(new QueueWorkerLoadError(assembly.FullName ?? assembly.GetName().Name ?? "unknown", ex));
            }
            catch (Exception ex)
            {
                if (options.ThrowOnLoadErrors)
                {
                    throw;
                }

                loadErrors.Add(new QueueWorkerLoadError(assembly.FullName ?? assembly.GetName().Name ?? "unknown", ex));
            }
        }

        services.AddSingleton(new QueueWorkerRegistry(discoveredWorkers, options.LogLoadErrors ? loadErrors : new List<QueueWorkerLoadError>()));

        services.AddHostedService<QueueWorkerStartupService>();

        return services;
    }

    private static ServiceLifetime DetermineServiceLifetime(QueueWorkerLifecycleAttribute? lifecycleAttr)
    {
        if (lifecycleAttr == null)
        {
            return ServiceLifetime.Singleton;
        }

        return lifecycleAttr.Lifecycle switch
        {
            WorkerLifecycle.Singleton => ServiceLifetime.Singleton,
            WorkerLifecycle.Scoped => ServiceLifetime.Scoped,
            WorkerLifecycle.Transient => ServiceLifetime.Transient,
            WorkerLifecycle.Pooled => ServiceLifetime.Singleton,
            _ => ServiceLifetime.Singleton
        };
    }
}

[Contract("QueueWorkerDiscoveryOptions", Version = "1.0", Namespace = "Framework.Queueing.Extensions")]
public class QueueWorkerDiscoveryOptions
{
    public bool ThrowOnLoadErrors { get; set; } = false;

    public bool LogLoadErrors { get; set; } = true;

    public List<Assembly> AdditionalAssemblies { get; } = new();
}

[Contract("QueueWorkerRegistration", Version = "1.0", Namespace = "Framework.Queueing.Extensions")]
public class QueueWorkerRegistration
{
    public required Type WorkerType { get; init; }

    public required List<QueueWorkerAttribute> QueueAttributes { get; init; }

    public QueueWorkerLifecycleAttribute? LifecycleAttribute { get; init; }
}

[Contract("QueueWorkerLoadError", Version = "1.0", Namespace = "Framework.Queueing.Extensions")]
public sealed class QueueWorkerLoadError
{
    public QueueWorkerLoadError(string assemblyName, Exception exception)
    {
        AssemblyName = assemblyName;
        Exception = exception;
    }

    public string AssemblyName { get; }

    public Exception Exception { get; }
}

public class QueueWorkerRegistry
{
    private readonly List<QueueWorkerRegistration> _registrations;
    private readonly List<QueueWorkerLoadError> _loadErrors;

    public QueueWorkerRegistry(
        List<QueueWorkerRegistration> registrations,
        List<QueueWorkerLoadError> loadErrors)
    {
        _registrations = registrations;
        _loadErrors = loadErrors;
    }

    public IReadOnlyList<QueueWorkerRegistration> GetRegistrations() => _registrations.AsReadOnly();

    public IReadOnlyList<QueueWorkerLoadError> GetLoadErrors() => _loadErrors.AsReadOnly();

    public int Count => _registrations.Count;
}
