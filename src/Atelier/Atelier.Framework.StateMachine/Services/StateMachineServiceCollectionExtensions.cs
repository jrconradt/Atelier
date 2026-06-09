using Atelier.Framework.StateMachine.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Atelier.Framework.StateMachine;

public static class StateMachineServiceCollectionExtensions
{
    public static IServiceCollection AddStateMachineServices(this IServiceCollection services)
    {
        services.AddScoped<IStateMachineFactory, StateMachineFactory>();
        services.AddScoped<IStateMachineTransitionService, StateMachineTransitionService>();
        services.AddScoped<IStateMachineRegistryService, StateMachineRegistryService>();
        services.AddScoped<IStateMachineRestoreService, StateMachineRestoreService>();
        services.AddSingleton<IStateMachineMonitoringService, StateMachineMonitoringService>();

        services.AddScoped<StateMachineOrchestrator>();

        services.AddHostedService(provider => provider.GetRequiredService<StateMachineOrchestrator>());

        services.AddSingleton<StateMachineRetentionService>();
        services.AddHostedService(provider => provider.GetRequiredService<StateMachineRetentionService>());

        return services;
    }
}
