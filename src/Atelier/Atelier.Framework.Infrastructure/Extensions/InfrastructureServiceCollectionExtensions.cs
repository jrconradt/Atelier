using Atelier.Framework.Primitives;
using System.Reflection;
using Atelier.Framework.Attributes;
using Atelier.Framework.Requisitions;
using Microsoft.Extensions.DependencyInjection;

namespace Atelier.Framework.Infrastructure.Extensions;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection RegisterDiscoveredServices(
        this IServiceCollection services,
        Action<InfrastructureRegistrationOptions>? configure = null)
    {
        var options = new InfrastructureRegistrationOptions();
        configure?.Invoke(options);

        if (options.AssembliesToScan == null
            || options.AssembliesToScan.Length == 0)
        {
            throw new InvalidOperationException(
                $"{nameof(RegisterDiscoveredServices)} requires an explicit assembly set via {nameof(InfrastructureRegistrationOptions)}.{nameof(InfrastructureRegistrationOptions.AssembliesToScan)}. Scanning all loaded AppDomain assemblies is not permitted.");
        }

        var assemblies = options.AssembliesToScan;

        foreach (var assembly in assemblies)
        {
            RegisterServicesFromAssembly(
                services,
                assembly,
                options);
        }

        return services;
    }

    private static void RegisterServicesFromAssembly(
        IServiceCollection services,
        Assembly assembly,
        InfrastructureRegistrationOptions options)
    {
        try
        {
            var types = assembly.GetTypes();

            foreach (var type in types)
            {
                if (!type.IsClass || type.IsAbstract)
                {
                    continue;
                }

                var infrastructureAttrs = type.GetCustomAttributes<InfrastructureAttribute>(inherit: false).ToArray();
                if (infrastructureAttrs.Length == 0)
                {
                    continue;
                }

                var infrastructureAttr = infrastructureAttrs[0];

                RegisterService(
                    services,
                    type,
                    infrastructureAttr,
                    options);
            }
        }
        catch (ReflectionTypeLoadException ex)
        {
            var assemblyName = assembly.GetName().Name;
            options.OnLoadError?.Invoke($"Failed to load types from assembly '{assemblyName}': {ex.Message}");
            if (ex.LoaderExceptions != null)
            {
                foreach (var loaderEx in ex.LoaderExceptions.Take(3))
                {
                    options.OnLoadError?.Invoke($"Loader exception in assembly '{assemblyName}': {loaderEx?.Message}");
                }
            }
            if (options.ThrowOnLoadErrors)
            {
                throw;
            }
        }
        catch (Exception ex)
        {
            options.OnLoadError?.Invoke($"Failed to scan assembly '{assembly.GetName().Name}': {ex.Message}");
            if (options.ThrowOnLoadErrors)
            {
                throw;
            }
        }
    }

    private static void RegisterService(
        IServiceCollection services,
        Type implementationType,
        InfrastructureAttribute attribute,
        InfrastructureRegistrationOptions options)
    {
        var serviceType = attribute.ServiceType ?? DetermineServiceType(implementationType);
        var lifetime = MapLifetime(attribute.Lifetime);

        if (options.BeforeRegistration != null)
        {
            var context = new ServiceRegistrationContext
            {
                ServiceType = serviceType,
                ImplementationType = implementationType,
                Lifetime = lifetime
            };

            if (!options.BeforeRegistration(context))
            {
                return;
            }
        }

        var descriptor = new ServiceDescriptor(
            serviceType,
            implementationType,
            lifetime);

        services.Add(descriptor);

        if (typeof(Microsoft.Extensions.Hosting.IHostedService).IsAssignableFrom(implementationType)
            && lifetime == ServiceLifetime.Singleton)
        {
            services.Add(new ServiceDescriptor(
                typeof(Microsoft.Extensions.Hosting.IHostedService),
                provider => (Microsoft.Extensions.Hosting.IHostedService)provider.GetRequiredService(serviceType),
                ServiceLifetime.Singleton));
        }
    }

    private static Type DetermineServiceType(Type implementationType)
    {
        var interfaces = implementationType.GetInterfaces();

        var candidateInterface = interfaces
            .FirstOrDefault(i => i.Name == $"I{implementationType.Name}");

        return candidateInterface ?? implementationType;
    }

    private static ServiceLifetime MapLifetime(InfrastructureLifetime lifetime)
    {
        return lifetime switch
        {
            InfrastructureLifetime.Singleton => ServiceLifetime.Singleton,
            InfrastructureLifetime.Scoped => ServiceLifetime.Scoped,
            InfrastructureLifetime.Transient => ServiceLifetime.Transient,
            _ => ServiceLifetime.Singleton
        };
    }
}

[Contract("InfrastructureRegistrationOptions", Version = "1.0", Namespace = "Framework.Infrastructure.Extensions")]
public partial class InfrastructureRegistrationOptions
{
    public Assembly[]? AssembliesToScan { get; set; }
    public bool ThrowOnLoadErrors { get; set; } = false;
    public Func<ServiceRegistrationContext, bool>? BeforeRegistration { get; set; }
    public Action<string>? OnLoadError { get; set; }
}

public class ServiceRegistrationContext
{
    public required Type ServiceType { get; set; }
    public required Type ImplementationType { get; set; }
    public required ServiceLifetime Lifetime { get; set; }
}
