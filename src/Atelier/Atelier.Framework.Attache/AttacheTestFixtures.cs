using System.Reflection;
using Atelier.Framework.Context;
using Atelier.Framework.Facility;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Testing;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Atelier.Framework.Attache;

[TestFixtureRegistry]
internal static class AttacheTestFixtures
{
    private const string HAPPY_CONSUMER = "atelier-happy";
    private const string HAPPY_TICKET = "atelier-happy";

    [Fixture(typeof(HealthCheckService))]
    internal static HealthCheckService HealthCheck()
    {
        return new FixtureHealthCheckService();
    }

    [Fixture(typeof(CapabilityRequest))]
    internal static CapabilityRequest Request()
    {
        return new CapabilityRequest
        {
            ConsumerId = HAPPY_CONSUMER,
            CapabilityTypeName = typeof(object).Name,
            CapabilityType = typeof(object)
        };
    }

    [Fixture(typeof(AttacheHost), Operation = "RequestCapabilityAsync")]
    internal static AttacheHost RequestReceiver()
    {
        return BuildHost(new ProvisioningRequisitionService(HAPPY_TICKET));
    }

    [Fixture(typeof(AttacheHost), Operation = "ReleaseCapabilityAsync")]
    internal static AttacheHost ReleaseReceiver()
    {
        var host = BuildHost(new ProvisioningRequisitionService(HAPPY_TICKET));

        var seated = host.RequestCapabilityAsync(Request(), CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        if (!seated.IsSuccess)
        {
            throw new InvalidOperationException(
                "Could not seat capability grant for release fixture.");
        }

        if (!string.Equals(seated.Data.TicketId, HAPPY_TICKET, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Seated grant ticket '{seated.Data.TicketId}' does not match expected '{HAPPY_TICKET}'.");
        }

        return host;
    }

    private static AttacheHost BuildHost(IRequisitionService requisitionService)
    {
        var ctor = typeof(AttacheHost)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

        var arguments = ctor.GetParameters()
            .Select(p => ResolveRequisite(p.ParameterType, requisitionService))
            .ToArray();

        return (AttacheHost)ctor.Invoke(arguments);
    }

    private static object ResolveRequisite(Type parameterType,
                                           IRequisitionService requisitionService)
    {
        if (parameterType == typeof(IRequisitionService))
        {
            return requisitionService;
        }
        if (parameterType == typeof(HealthCheckService))
        {
            return new FixtureHealthCheckService();
        }
        return AutoMockProvider.For(parameterType)!;
    }

    private sealed class FixtureHealthCheckService : HealthCheckService
    {
        public override Task<HealthReport> CheckHealthAsync(
            Func<HealthCheckRegistration, bool>? predicate,
            CancellationToken cancellationToken = default)
        {
            var report = new HealthReport(
                new Dictionary<string, HealthReportEntry>(),
                HealthStatus.Healthy,
                TimeSpan.Zero);

            return Task.FromResult(report);
        }
    }


    private sealed class ProvisioningRequisitionService : IRequisitionService
    {
        private readonly string _ticketId;

        public ProvisioningRequisitionService(string ticketId)
        {
            _ticketId = ticketId;
        }

        public Task<Outcome<ProvisionTicket>> ProvisionAsync(
            IRequirement requirement,
            CancellationToken cancellationToken)
        {
            var ticket = new ProvisionTicket
            {
                TicketId = _ticketId,
                RequirementId = requirement.RequirementId,
                FacilityId = "atelier-happy-facility",
                Scope = requirement.Scope
            };

            return Task.FromResult(Outcome<ProvisionTicket>.Success(ticket));
        }

        public Task<Outcome> ReleaseAsync(
            string requirementId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Outcome.Success());
        }

        public IEnumerable<ActiveRequisition> GetActiveRequisitions()
        {
            return Array.Empty<ActiveRequisition>();
        }
    }
}
