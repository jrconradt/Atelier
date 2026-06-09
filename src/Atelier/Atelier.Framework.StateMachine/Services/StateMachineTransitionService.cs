using Atelier.Framework.Primitives;
using Atelier.Framework.Infrastructure;
using Atelier.Framework.Attributes;
using Atelier.Framework.Observability;
using Atelier.Framework.Infrastructure.Operation;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;

namespace Atelier.Framework.StateMachine.Services;

[Infrastructure(InfrastructureLifetime.Scoped)]
public partial class StateMachineTransitionService : IAtelier, IStateMachineTransitionService
{
    [Requisite] protected readonly IStateMachineRegistryService _registryService = null!;

    [Operation("TransitionStateMachine")]
    public async Task<Outcome> TransitionStateMachineAsync(
        string instanceId,
        string transitionName,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome.Failure();
        }

        if (string.IsNullOrWhiteSpace(instanceId))
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Instance ID was null or empty")]);
            return Outcome.Failure();
        }

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "StateMachine", instanceId);

        if (string.IsNullOrWhiteSpace(transitionName))
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Transition name was null or empty"), ("InstanceId", instanceId)]);
            return Outcome.Failure();
        }

        var instance = _registryService.GetInstance(instanceId);
        if (instance == null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "State machine not found"), ("InstanceId", instanceId)]);
            return Outcome.Failure();
        }

        Observe(
            LogLevel.Information,
            null, values: [("Message", "Executing transition on state machine"), ("InstanceId", instanceId), ("TransitionName", transitionName), ("CurrentState", instance.CurrentState)]);

        var transitionResult = await instance.ExecuteTransitionAsync(transitionName, cancellationToken).ConfigureAwait(false);

        if (transitionResult.IsSuccess)
        {
            Observe(
                LogLevel.Information,
                null, values: [("Message", "Successfully executed transition"), ("InstanceId", instanceId), ("TransitionName", transitionName), ("NewState", instance.CurrentState)]);
        }
        else
        {
            Observe(
                LogLevel.Warning,
                null, values: [("Reason", "Transition execution failed"), ("InstanceId", instanceId), ("TransitionName", transitionName)]);
        }

        return transitionResult;
    }

    [Operation("GetValidTransitions")]
    public async Task<Outcome<IEnumerable<string>>> GetValidTransitionsAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        if (instanceId is null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Instance ID was null")]);
            return Outcome<IEnumerable<string>>.Failure();
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome<IEnumerable<string>>.Failure();
        }

        if (string.IsNullOrWhiteSpace(instanceId))
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Instance ID was empty")]);
            return Outcome<IEnumerable<string>>.Failure();
        }

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "StateMachine", instanceId);

        var instance = _registryService.GetInstance(instanceId);
        if (instance == null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "State machine not found"), ("InstanceId", instanceId)]);
            return Outcome<IEnumerable<string>>.Failure();
        }

        return Outcome<IEnumerable<string>>.Success(instance.GetValidTransitions());
    }
}
