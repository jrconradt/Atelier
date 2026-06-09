using System.Reflection;
using Atelier.Framework.EventStream.Configuration;
using Atelier.Framework.EventStream.Consumers;
using Atelier.Framework.EventStream.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Atelier.Framework.EventStream.Extensions;

public static class EventStreamServiceCollectionExtensions
{
    public static IServiceCollection AddEventStreaming(
        this IServiceCollection services,
        Action<EventStreamOptions>? configure = null)
    {
        if (services.Any(d => d.ServiceType == typeof(EventStreamMarker)))
        {
            return services;
        }

        services.AddSingleton<EventStreamMarker>();

        var options = new EventStreamOptions();
        configure?.Invoke(options);

        services.AddSingleton<IValidateOptions<EventStreamOptions>, EventStreamOptionsValidator>();
        services.AddOptions<EventStreamOptions>()
            .ValidateOnStart();

        services.Configure<EventStreamOptions>(opts =>
        {
            opts.AutoDiscoverConsumers = options.AutoDiscoverConsumers;
            opts.EnableHealthChecks = options.EnableHealthChecks;
            opts.HealthCheckIntervalMs = options.HealthCheckIntervalMs;
            opts.ConsumerBatchSize = options.ConsumerBatchSize;
            opts.ConsumerCommitInterval = options.ConsumerCommitInterval;
            opts.ConsumerIdlePollDelayMs = options.ConsumerIdlePollDelayMs;
            opts.ConsumerErrorBackoffMs = options.ConsumerErrorBackoffMs;
            opts.ConsumerMaxRetries = options.ConsumerMaxRetries;
            opts.ConsumerMaxRestarts = options.ConsumerMaxRestarts;
            opts.ConsumerRestartBackoffMs = options.ConsumerRestartBackoffMs;
            opts.ConsumerRestartBackoffMaxMs = options.ConsumerRestartBackoffMaxMs;
            opts.OffsetStoreDirectory = options.OffsetStoreDirectory;
            opts.HashRegistryDirectory = options.HashRegistryDirectory;
        });

        services.TryAddSingleton<EventStreamOffsetStore>();
        services.TryAddSingleton<IEventStreamOffsetStore>(sp => sp.GetRequiredService<EventStreamOffsetStore>());

        if (options.AutoDiscoverConsumers)
        {
            DiscoverAndRegisterConsumers(services);
        }

        if (options.EnableHealthChecks)
        {
            RegisterHealthChecks(services, options);
        }

        return services;
    }

    private static void RegisterHealthChecks(
        IServiceCollection services,
        EventStreamOptions options)
    {
        services.AddHealthChecks()
            .AddCheck<Health.EventStreamHealthCheck>(
                "event-stream",
                tags: new[] { "event-stream", "messaging", "readiness" });

        services.TryAddSingleton<Health.EventStreamHealthCheckPublisher>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthCheckPublisher>(
            sp => sp.GetRequiredService<Health.EventStreamHealthCheckPublisher>()));

        services.Configure<HealthCheckPublisherOptions>(publisher =>
        {
            publisher.Period = TimeSpan.FromMilliseconds(options.HealthCheckIntervalMs);
        });
    }

    private static void DiscoverAndRegisterConsumers(IServiceCollection services)
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        foreach (var assembly in assemblies)
        {
            Type[] candidateTypes;
            try
            {
                candidateTypes = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                candidateTypes = ex.Types
                    .Where(t => t != null)
                    .Cast<Type>()
                    .ToArray();
            }

            var consumerTypes = candidateTypes
                .Where(t => !t.IsAbstract && !t.IsInterface)
                .Where(t => typeof(IEventStreamConsumer).IsAssignableFrom(t));

            foreach (var consumerType in consumerTypes)
            {
                services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(IEventStreamConsumer), consumerType));
            }
        }
    }
}

internal sealed class EventStreamMarker
{
}
