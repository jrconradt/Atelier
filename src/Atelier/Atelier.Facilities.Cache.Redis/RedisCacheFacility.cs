using Atelier.Framework.Primitives;
using System.Collections.Concurrent;
using Atelier.Framework.Facility;
using Atelier.Framework.Attributes;
using Atelier.Framework.Network;
using Atelier.Framework.Observability;
using Atelier.Framework.Offering.Requisition;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;
using Atelier.Framework.Host.Execution;

namespace Atelier.Facilities.Cache.Redis;

[Infrastructure(typeof(IFacility),
                typeof(RedisCacheFacility),
                InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class RedisCacheFacility : FacilityBase
{
    [Requisite] protected readonly IOfferingRequisitionService _offeringRequisitions = null!;
    [Requisite] protected readonly IRedisConnectionProvider _connection = null!;

    private const string REDIS_IMAGE = "redis";
    private const int REDIS_PORT = 6379;

    private readonly ConcurrentDictionary<string, string> _ticketRequisitions = new();

    public override string FacilityId => "facility-cache-redis";
    public override string FacilityName => "Redis Cache Provider";
    public override FacilityType Type => FacilityType.NetworkMapped;

    protected override void InitializeCapabilities()
    {
        Capabilities.Zone = typeof(Atelier.Framework.Primitives.Data);
        Capabilities.SupportedScopes.Add(RequirementScope.Capability);
        Capabilities.CanProvide.Add(typeof(ICache));
    }

    public override bool CanFulfill(IRequirement requirement)
    {
        if (!Capabilities.SupportedScopes.Contains(requirement.Scope))
        {
            return false;
        }

        return requirement.RequiredType == typeof(ICache);
    }

    public override Task<Outcome<ResourceAvailability>> CheckResourceAvailabilityAsync(
        ResourceAllocation requested,
        CancellationToken cancellationToken)
    {
        var ready = !_connection.IsConfigured || _connection.IsConnected;

        var availability = new ResourceAvailability
        {
            IsAvailable = ready,
            Available = Capabilities.CurrentAvailable
        };

        return Task.FromResult(Outcome<ResourceAvailability>.Success(availability));
    }

    public override async Task<Outcome<ProvisionTicket>> ProvisionAsync(
        IRequirement requirement,
        CancellationToken cancellationToken)
    {
        if (!CanFulfill(requirement))
        {
            Observe(LogLevel.Warning, values: [("RequirementId", requirement.RequirementId), ("RequiredType", requirement.RequiredType.Name), ("Reason", "Facility cannot fulfill the requirement")]);
            return Outcome<ProvisionTicket>.Failure();
        }

        var request = new OfferingRequisitionRequest
        {
            OfferingType = typeof(RedisCache),
            RequesterId = requirement.RequirementId,
            RequesterType = typeof(ICache),
            PreferredExecutionMode = OfferingExecutionMode.NetworkMapped,
            PlacementStrategy = ZonePlacementStrategy.AllowCrossZone,
            ResourceRequirements = requirement.ResourceNeeds,
            Backing = new BackingOfferingDescriptor
            {
                ImageName = REDIS_IMAGE,
                ContainerNamePrefix = "atelier-cache-redis",
                ExposedPorts = { REDIS_PORT },
                ReadyProbePort = REDIS_PORT
            }
        };

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "Requirement", requirement.RequirementId);

        var requisition = await _offeringRequisitions.RequisitionOfferingAsync(
            request,
            cancellationToken).ConfigureAwait(false);

        if (!requisition.IsSuccess)
        {
            Observe(LogLevel.Warning, values: [("RequirementId", requirement.RequirementId), ("Reason", "Underlying offering requisition failed")]);
            return Outcome<ProvisionTicket>.Failure();
        }

        var result = requisition.Data;

        if (!string.IsNullOrWhiteSpace(result.NetworkAddress))
        {
            var port = result.NetworkPort ?? REDIS_PORT;
            Exception? connectException = null;
            Outcome<IRedisConnectionProvider> configured = default;
            try
            {
                configured = _connection.Configure($"{result.NetworkAddress}:{port}");
            }
            catch (Exception ex)
            {
                connectException = ex;
            }

            if (connectException is not null
                || !configured.IsSuccess)
            {
                await _offeringRequisitions.ReleaseRequisitionAsync(
                    result.RequisitionId,
                    cancellationToken).ConfigureAwait(false);

                Observe(LogLevel.Error,
                        connectException, values: [("RequirementId", requirement.RequirementId), ("Endpoint", result.NetworkAddress), ("Port", port), ("Reason", "Failed to connect to provisioned Redis endpoint")]);

                return Outcome<ProvisionTicket>.Failure();
            }
        }

        var ticket = new ProvisionTicket
        {
            RequirementId = requirement.RequirementId,
            FacilityId = FacilityId,
            Scope = requirement.Scope,
            GatewayEndpoint = result.NetworkAddress,
            GatewayPort = result.NetworkPort,
            Zone = typeof(Atelier.Framework.Primitives.Data),
            AllocatedResources = result.AllocatedResources,
            Status = ProvisionStatus.Provisioned
        };

        _ticketRequisitions[ticket.TicketId] = result.RequisitionId;

        Observe(LogLevel.Information, values: [("RequirementId", requirement.RequirementId), ("TicketId", ticket.TicketId), ("Endpoint", result.NetworkAddress ?? "none"), ("Port", result.NetworkPort ?? 0)]);

        return ticket;
    }

    public override async Task<Outcome> ReleaseAsync(
        string ticketId,
        CancellationToken cancellationToken)
    {
        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "ProvisionTicket", ticketId);

        if (_ticketRequisitions.TryRemove(ticketId, out var requisitionId))
        {
            var released = await _offeringRequisitions.ReleaseRequisitionAsync(
                requisitionId,
                cancellationToken).ConfigureAwait(false);

            if (!released.IsSuccess)
            {
                _ticketRequisitions.TryAdd(ticketId, requisitionId);
                Observe(LogLevel.Warning, values: [("TicketId", ticketId), ("Reason", "Underlying requisition release failed; ticket mapping restored")]);
            }

            return released;
        }

        Observe(LogLevel.Information, values: [("TicketId", ticketId), ("Reason", "Release of absent ticket treated as success")]);
        return Outcome.Success();
    }
}
