using Atelier.Framework.Primitives;
using Atelier.Framework.Facility.Configuration;
using Atelier.Framework.Attributes;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Host.Execution;

namespace Atelier.Framework.Facility.Remote;

[Infrastructure(typeof(IFacility),
                typeof(RemoteFacility),
                InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class RemoteFacility : FacilityBase, IAtelier
{
    private readonly RemoteFacilityState _state = new();

    public override string FacilityId => _state.Descriptor.FacilityId;
    public override string FacilityName => _state.Descriptor.FacilityName;
    public override FacilityType Type => FacilityType.NetworkMapped;

    public RemoteFacility() { }

    public RemoteFacility Configure(
        RemoteFacilityDescriptor descriptor,
        RemoteFacilityConfiguration? config = null)
    {
        _state.Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        InitializeCapabilities();
        _state.HealthProbe?.Dispose();
        _state.HealthProbe = new RemoteFacilityHealthProbe(_state.Descriptor, config);
        return this;
    }

    protected override void InitializeCapabilities()
    {
        Capabilities.SupportedScopes.UnionWith(_state.Descriptor.Capabilities.SupportedScopes);
        Capabilities.CanProvide.UnionWith(_state.Descriptor.Capabilities.CanProvide);
        Capabilities.TotalCapacity = _state.Descriptor.Capabilities.TotalCapacity;
        Capabilities.CurrentAvailable = _state.Descriptor.Capabilities.CurrentAvailable;
        Capabilities.Zone = _state.Descriptor.Capabilities.Zone;
        Capabilities.Operations.UnionWith(_state.Descriptor.Capabilities.Operations);

        foreach (var kvp in _state.Descriptor.Capabilities.Metadata)
        {
            Capabilities.Metadata[kvp.Key] = kvp.Value;
        }

        foreach (var kvp in _state.Descriptor.Capabilities.Contracts)
        {
            Capabilities.Contracts[kvp.Key] = kvp.Value;
        }
    }

    public override bool CanFulfill(IRequirement requirement)
    {
        if (!Capabilities.SupportedScopes.Contains(requirement.Scope))
        {
            return false;
        }

        if (requirement is Requirement<object> genericReq)
        {
            var requiredType = genericReq.GetType().GetGenericArguments()[0];
            if (!Capabilities.CanProvide.Contains(requiredType))
            {
                return false;
            }
        }

        return true;
    }

    public override async Task<Outcome<ResourceAvailability>> CheckResourceAvailabilityAsync(
        ResourceAllocation requested,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        if (!_state.Descriptor.IsHealthy)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Remote facility is unhealthy"), ("FacilityId", FacilityId), ("Endpoint", _state.Descriptor.Endpoint)]);
            return Outcome<ResourceAvailability>.Failure();
        }

        var available = new ResourceAvailability
        {
            IsAvailable = true,
            Available = Capabilities.CurrentAvailable
        };

        return available;
    }

    public override Task<Outcome<ProvisionTicket>> ProvisionAsync(
        IRequirement requirement,
        CancellationToken cancellationToken)
    {
        if (!TryAllocateResources(
                requirement.RequirementId,
                requirement.ResourceNeeds,
                out var allocated))
        {
            LogAllocationFailure(requirement);
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Insufficient resources for remote facility"), ("FacilityId", FacilityId), ("RequirementId", requirement.RequirementId)]);
            return Task.FromResult(Outcome<ProvisionTicket>.Failure());
        }

        var ticket = new ProvisionTicket
        {
            RequirementId = requirement.RequirementId,
            FacilityId = FacilityId,
            Scope = requirement.Scope,
            GatewayEndpoint = _state.Descriptor.Endpoint,
            Zone = Capabilities.Zone ?? typeof(Atelier.Framework.Primitives.Internal),
            AllocatedResources = allocated,
            Status = ProvisionStatus.Provisioned
        };

        Observe(LogLevel.Information, values: [("FacilityId", FacilityId), ("RequirementId", requirement.RequirementId), ("RequiredType", requirement.RequiredType.Name), ("Endpoint", _state.Descriptor.Endpoint)]);

        return Task.FromResult(Outcome<ProvisionTicket>.Success(ticket));
    }

    public override async Task<Outcome> ReleaseAsync(
        string furnishingId,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        ReleaseResources(furnishingId);

        Observe(LogLevel.Information, values: [("FacilityId", FacilityId), ("FurnishingId", furnishingId)]);

        return Outcome.Success();
    }

    public override void Dispose()
    {
        _state.HealthProbe?.Dispose();
        _state.HealthProbe = null;
        base.Dispose();
    }
}
